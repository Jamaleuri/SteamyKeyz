using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SteamyKeyz.Models;

public class EmailJob
{
    [Key]
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    /// <summary>
    /// Type of email: "Keys" or "Invoice"
    /// </summary>
    [Required, MaxLength(20)]
    public string EmailType { get; set; } = string.Empty;

    /// <summary>
    /// Recipient email address (captured at creation time so we don't need to reload the user).
    /// </summary>
    [Required, MaxLength(255)]
    public string ToEmail { get; set; } = string.Empty;

    /// <summary>
    /// JSON payload containing all the data needed to send the email.
    /// This avoids any EF entity references in the background worker.
    /// </summary>
    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// When this job becomes eligible for processing.
    /// For keys emails, this is CreatedAt + 10 minutes.
    /// For invoice emails, this is immediately (CreatedAt).
    /// </summary>
    public DateTime ScheduledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Pending → Sent / Failed
    /// </summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Error message if sending failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of times this job has been attempted.
    /// </summary>
    public int Attempts { get; set; } = 0;

    [ForeignKey(nameof(InvoiceId))]
    public Invoice Invoice { get; set; } = null!;
}