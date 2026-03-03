using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class InvoiceItem
{
    [Key]
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public int KeyId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Range(0, double.MaxValue)]
    public decimal PriceAtPurchase { get; set; }

    [ForeignKey(nameof(InvoiceId))]
    public Invoice Invoice { get; set; } = null!;

    [ForeignKey(nameof(KeyId))]
    public Key Key { get; set; } = null!;
}