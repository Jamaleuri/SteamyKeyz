using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Invoice
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required, MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Column(TypeName = "decimal(12,2)")]
    [Range(0, double.MaxValue)]
    public decimal TotalAmount { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    public ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
}