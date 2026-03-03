using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("Keys")]
public class Key
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }

    public int PlatformId { get; set; }

    [Required, MaxLength(255)]
    public string KeyValue { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Available";

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(GameId))]
    public Game Game { get; set; } = null!;

    [ForeignKey(nameof(PlatformId))]
    public Platform Platform { get; set; } = null!;

    public InvoiceItem? InvoiceItem { get; set; }
}