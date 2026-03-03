using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SteamyKeyz.Models;

public class CartItem
{
    [Key]
    public int Id { get; set; }

    public int ShoppingCartId { get; set; }

    public int GameId { get; set; }

    public int PlatformId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    [ForeignKey(nameof(ShoppingCartId))]
    public ShoppingCart ShoppingCart { get; set; } = null!;

    [ForeignKey(nameof(GameId))]
    public Game Game { get; set; } = null!;

    [ForeignKey(nameof(PlatformId))]
    public Platform Platform { get; set; } = null!;
}