using System.Text;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using SteamyKeyz.Settings;

namespace SteamyKeyz.Services;

public class EmailService : IEmailService
{
    private readonly SmtpSettings _smtp;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<SmtpSettings> smtp,
        IWebHostEnvironment env,
        ILogger<EmailService> logger)
    {
        _smtp = smtp.Value;
        _env = env;
        _logger = logger;
    }

    // ─── Low-level: send any HTML email ──────────────────────────

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();

        // MailHog doesn't use TLS, so we connect without SSL.
        // For a real provider (SendGrid, etc.) you'd set EnableSsl = true
        // and call AuthenticateAsync with credentials.
        await client.ConnectAsync(_smtp.Host, _smtp.Port, _smtp.EnableSsl);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Email sent to {To} — subject: {Subject}", toEmail, subject);
    }

    // ─── Invoice email ───────────────────────────────────────────

    public async Task SendInvoiceEmailAsync(string toEmail, InvoiceEmailModel model)
    {
        // 1. Load the HTML template from disk
        var template = await LoadTemplateAsync("InvoiceEmailTemplate.html");

        // 2. Build the repeated item rows
        var itemRowsHtml = new StringBuilder();
        foreach (var item in model.Items)
        {
            itemRowsHtml.Append(BuildInvoiceItemRow(item));
        }

        // 3. Replace all placeholders
        var html = template
            .Replace("{{InvoiceNumber}}", Sanitize(model.InvoiceNumber))
            .Replace("{{InvoiceDate}}", Sanitize(model.InvoiceDate))
            .Replace("{{CustomerName}}", Sanitize(model.CustomerName))
            .Replace("{{CustomerEmail}}", Sanitize(model.CustomerEmail))
            .Replace("{{CustomerAddress}}", Sanitize(model.CustomerAddress))
            .Replace("{{Subtotal}}", Sanitize(model.Subtotal))
            .Replace("{{TaxAmount}}", Sanitize(model.TaxAmount))
            .Replace("{{TotalAmount}}", Sanitize(model.TotalAmount));

        // 4. Replace the loop section with the actual rows
        //    Everything between {{#InvoiceItems}} and {{/InvoiceItems}} is
        //    the "template row" — we replace that entire block with our built rows.
        html = ReplaceBetween(html, "{{#InvoiceItems}}", "{{/InvoiceItems}}", itemRowsHtml.ToString());

        // 5. Send
        await SendEmailAsync(toEmail, $"SteamyKeyz Invoice #{model.InvoiceNumber}", html);
    }

    // ─── Keys email ──────────────────────────────────────────────

    public async Task SendKeysEmailAsync(string toEmail, KeysEmailModel model)
    {
        var template = await LoadTemplateAsync("KeysEmailTemplate.html");

        // Build key cards
        var keyCardsHtml = new StringBuilder();
        foreach (var key in model.Keys)
        {
            keyCardsHtml.Append(BuildKeyCard(key));
        }

        var html = template
            .Replace("{{CustomerName}}", Sanitize(model.CustomerName))
            .Replace("{{InvoiceNumber}}", Sanitize(model.InvoiceNumber))
            .Replace("{{AccountUrl}}", Sanitize(model.AccountUrl));

        // Replace the key loop section
        html = ReplaceBetween(html, "{{#Keys}}", "{{/Keys}}", keyCardsHtml.ToString());

        // Conditionally show/hide the "View in My Account" block
        if (model.IsRegisteredUser)
        {
            html = html
                .Replace("{{#IsRegisteredUser}}", "")
                .Replace("{{/IsRegisteredUser}}", "");
        }
        else
        {
            html = ReplaceBetween(html, "{{#IsRegisteredUser}}", "{{/IsRegisteredUser}}", "");
        }

        await SendEmailAsync(toEmail, $"Your SteamyKeyz License Keys — Order #{model.InvoiceNumber}", html);
    }

    // ═════════════════════════════════════════════════════════════
    //  Private helpers
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads an HTML template from wwwroot/email-templates/{fileName}.
    /// </summary>
    private async Task<string> LoadTemplateAsync(string fileName)
    {
        var path = Path.Combine(_env.WebRootPath, "email-templates", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Email template not found: {path}");

        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// Builds one table row for an invoice line item.
    /// This HTML matches the structure in InvoiceEmailTemplate.html.
    /// </summary>
    private static string BuildInvoiceItemRow(InvoiceEmailItem item)
    {
        return $@"
            <tr>
                <td style=""padding:14px 12px; color:#e0e0ec; font-size:14px; font-weight:600; border-bottom:1px solid #1f1f2e;"">
                    {Sanitize(item.GameTitle)}
                </td>
                <td style=""padding:14px 12px; text-align:center; border-bottom:1px solid #1f1f2e;"">
                    <span style=""display:inline-block; background-color:#2d2d40; color:#a0a0b8; font-size:11px; font-weight:700; padding:3px 10px; border-radius:4px; text-transform:uppercase; letter-spacing:0.5px;"">
                        {Sanitize(item.PlatformName)}
                    </span>
                </td>
                <td style=""padding:14px 12px; color:#a0a0b8; font-size:14px; text-align:center; border-bottom:1px solid #1f1f2e;"">
                    {item.Quantity}
                </td>
                <td style=""padding:14px 12px; color:#00b894; font-size:14px; font-weight:700; text-align:right; border-bottom:1px solid #1f1f2e;"">
                    €{Sanitize(item.Price)}
                </td>
            </tr>";
    }

    /// <summary>
    /// Builds one key card block for the keys email.
    /// This HTML matches the structure in KeysEmailTemplate.html.
    /// </summary>
    private static string BuildKeyCard(KeyEmailItem key)
    {
        return $@"
            <tr>
                <td style=""padding:0 40px 16px 40px;"">
                    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background-color:#13131c; border-radius:10px; overflow:hidden; border:1px solid #2d2d40;"">
                        <tr>
                            <td style=""padding:16px 20px 12px 20px;"">
                                <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                                    <tr>
                                        <td valign=""middle"">
                                            <span style=""font-size:16px; font-weight:700; color:#ffffff;"">
                                                {Sanitize(key.GameTitle)}
                                            </span>
                                        </td>
                                        <td valign=""middle"" style=""text-align:right;"">
                                            <span style=""display:inline-block; background-color:#2d2d40; color:#a0a0b8; font-size:11px; font-weight:700; padding:4px 12px; border-radius:4px; text-transform:uppercase; letter-spacing:0.5px;"">
                                                {Sanitize(key.PlatformName)}
                                            </span>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                        <tr>
                            <td style=""padding:0 20px 16px 20px;"">
                                <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%); border-radius:8px; border:1px dashed #334155;"">
                                    <tr>
                                        <td style=""padding:16px 20px; text-align:center;"">
                                            <span style=""font-family:'Courier New',Courier,monospace; font-size:20px; font-weight:700; color:#00b894; letter-spacing:2px; word-break:break-all;"">
                                                {Sanitize(key.KeyValue)}
                                            </span>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </td>
            </tr>";
    }

    /// <summary>
    /// Replaces everything between (and including) startMarker and endMarker
    /// with the replacement string. Handles HTML comment wrappers too.
    /// </summary>
    private static string ReplaceBetween(string source, string startMarker, string endMarker, string replacement)
    {
        // Find the start of the line containing the startMarker
        var startIdx = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx < 0) return source;

        // Walk back to the start of the comment line (<!-- {{#...}} -->)
        var lineStart = source.LastIndexOf('\n', startIdx);
        if (lineStart < 0) lineStart = 0; else lineStart++;

        // Find the end of the line containing the endMarker
        var endIdx = source.IndexOf(endMarker, startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return source;

        var lineEnd = source.IndexOf('\n', endIdx);
        if (lineEnd < 0) lineEnd = source.Length; else lineEnd++;

        return string.Concat(source.AsSpan(0, lineStart), replacement, source.AsSpan(lineEnd));
    }

    /// <summary>
    /// Basic HTML-encode to prevent XSS in email content.
    /// </summary>
    private static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return System.Net.WebUtility.HtmlEncode(input);
    }
}