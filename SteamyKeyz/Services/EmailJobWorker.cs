using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;

namespace SteamyKeyz.Services;

public class EmailJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailJobWorker> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    public EmailJobWorker(IServiceScopeFactory scopeFactory, ILogger<EmailJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailJobWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingJobsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EmailJobWorker encountered an error during processing.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("EmailJobWorker stopped.");
    }

    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;

        // Grab up to 10 jobs that are due
        var jobs = await context.EmailJobs
            .Where(j => j.Status == "Pending" && j.ScheduledAt <= now && j.Attempts < 3)
            .OrderBy(j => j.ScheduledAt)
            .Take(10)
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            job.Attempts++;

            try
            {
                switch (job.EmailType)
                {
                    case "Invoice":
                        var invoiceModel = JsonSerializer.Deserialize<InvoiceEmailModel>(job.PayloadJson)!;
                        await emailService.SendInvoiceEmailAsync(job.ToEmail, invoiceModel);

                        // Update invoice status to InvoiceSent
                        var invoiceForStatus = await context.Invoices.FindAsync(new object[] { job.InvoiceId }, ct);
                        if (invoiceForStatus is not null && invoiceForStatus.Status == "Paid")
                        {
                            var oldStatus = invoiceForStatus.Status;
                            invoiceForStatus.Status = "InvoiceSent";
                            context.OrderStatusHistory.Add(new OrderStatusHistory
                            {
                                InvoiceId = job.InvoiceId,
                                OldStatus = oldStatus,
                                NewStatus = "InvoiceSent",
                                ChangedBy = "System",
                                Notes = "Invoice email sent by background worker"
                            });
                            _logger.LogInformation("Invoice {Id} status → InvoiceSent", job.InvoiceId);
                        }
                        break;

                    case "Keys":
                        var keysModel = JsonSerializer.Deserialize<KeysEmailModel>(job.PayloadJson)!;
                        await emailService.SendKeysEmailAsync(job.ToEmail, keysModel);

                        // Update invoice status to KeysSent
                        var invoiceForKeys = await context.Invoices.FindAsync(new object[] { job.InvoiceId }, ct);
                        if (invoiceForKeys is not null && invoiceForKeys.Status is "Paid" or "InvoiceSent")
                        {
                            var oldKeysStatus = invoiceForKeys.Status;
                            invoiceForKeys.Status = "KeysSent";
                            context.OrderStatusHistory.Add(new OrderStatusHistory
                            {
                                InvoiceId = job.InvoiceId,
                                OldStatus = oldKeysStatus,
                                NewStatus = "KeysSent",
                                ChangedBy = "System",
                                Notes = "Keys email sent by background worker"
                            });
                            _logger.LogInformation("Invoice {Id} status → KeysSent", job.InvoiceId);
                        }
                        break;

                    default:
                        _logger.LogWarning("Unknown email job type: {Type}", job.EmailType);
                        break;
                }

                job.Status = "Sent";
                job.ProcessedAt = DateTime.UtcNow;
                _logger.LogInformation("EmailJob {Id} ({Type}) sent to {To}",
                    job.Id, job.EmailType, job.ToEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EmailJob {Id} failed (attempt {Attempt})",
                    job.Id, job.Attempts);

                job.ErrorMessage = ex.Message;

                if (job.Attempts >= 3)
                {
                    job.Status = "Failed";
                    job.ProcessedAt = DateTime.UtcNow;
                }
                // else stays "Pending" and will be retried next cycle
            }

            await context.SaveChangesAsync(ct);
        }
    }
}