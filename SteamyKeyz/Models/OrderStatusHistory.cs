using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class OrderStatusHistory
{
    [Key]
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    [Required, MaxLength(20)]
    public string OldStatus { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string NewStatus { get; set; } = string.Empty;

    /// <summary>
    /// Who triggered the change: "System", "Admin", or a username.
    /// </summary>
    [Required, MaxLength(100)]
    public string ChangedBy { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Notes { get; set; }

    [ForeignKey(nameof(InvoiceId))]
    public Invoice Invoice { get; set; } = null!;
}