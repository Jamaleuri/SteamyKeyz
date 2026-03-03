using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

public class Game
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required, MaxLength(255)]
    public string Developer { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Publisher { get; set; } = string.Empty;

    [Column(TypeName = "date")]
    public DateTime? ReleaseDate { get; set; }

    // Computed image path — not stored in DB
    [NotMapped]
    public string ImageUrl => $"/images/games/{Id}/cover.jpg";

    public ICollection<GamePlatform> GamePlatforms { get; set; } = new List<GamePlatform>();
    public ICollection<Key> Keys { get; set; } = new List<Key>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}