using System.ComponentModel.DataAnnotations;

namespace SteamyKeyz.ViewModels;

// ── Cart page ───────────────────────────────────────────────────

public class CartViewModel
{
    public List<CartItemViewModel> Items { get; set; } = new();
    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public int TotalItems => Items.Sum(i => i.Quantity);
}

public class CartItemViewModel
{
    public int GameId { get; set; }
    public int PlatformId { get; set; }
    public string GameTitle { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int AvailableStock { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

// ── Checkout ────────────────────────────────────────────────────

public class CheckoutViewModel
{
    public CartViewModel Cart { get; set; } = new();

    // Guest fields (not needed when logged in)
    public bool IsGuest { get; set; }

    [MaxLength(100)]
    [Display(Name = "Full Name")]
    public string? GuestName { get; set; }

    [MaxLength(255)]
    [EmailAddress]
    [Display(Name = "Email")]
    public string? GuestEmail { get; set; }

    [MaxLength(255)]
    [Display(Name = "Street Address")]
    public string? Street { get; set; }

    [MaxLength(20)]
    [Display(Name = "Postal Code")]
    public string? PostalCode { get; set; }

    [MaxLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [MaxLength(100)]
    [Display(Name = "Country")]
    public string? Country { get; set; }

    // Optional business fields
    [MaxLength(255)]
    [Display(Name = "Company (optional)")]
    public string? Company { get; set; }

    [MaxLength(50)]
    [Display(Name = "VAT ID (optional)")]
    public string? VatId { get; set; }
}

// ── Order confirmation ──────────────────────────────────────────

public class OrderConfirmationViewModel
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRegisteredUser { get; set; }

    public List<OrderItemViewModel> Items { get; set; } = new();
}

public class OrderItemViewModel
{
    public string GameTitle { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? KeyValue { get; set; } // shown only after keys are delivered
}
