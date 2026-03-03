using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class GamePlatform
{
    public int GameId { get; set; }

    public int PlatformId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [ForeignKey(nameof(GameId))]
    public Game Game { get; set; } = null!;

    [ForeignKey(nameof(PlatformId))]
    public Platform Platform { get; set; } = null!;
}