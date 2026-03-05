using System.ComponentModel.DataAnnotations;

namespace SteamyKeyz.ViewModels;

// ── Admin: Order list ───────────────────────────────────────────

public class AdminOrderIndexViewModel
{
    public List<AdminOrderSummaryViewModel> Orders { get; set; } = new();

    // Filters
    public string? Status { get; set; }
    public string? Search { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    // Stats
    public int TotalOrders { get; set; }
    public int PendingCount { get; set; }
    public int PaidCount { get; set; }
    public int KeysSentCount { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class AdminOrderSummaryViewModel
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsGuestOrder { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int ItemCount { get; set; }
    public List<StatusHistoryItemViewModel> StatusHistory { get; set; } = new();
}

// ── Admin: Order detail ─────────────────────────────────────────

public class AdminOrderDetailViewModel
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<StatusHistoryItemViewModel> StatusHistory { get; set; } = new();

    // Customer info
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsGuestOrder { get; set; }

    public List<AdminOrderItemViewModel> Items { get; set; } = new();
}

public class AdminOrderItemViewModel
{
    public int KeyId { get; set; }
    public string GameTitle { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public decimal PriceAtPurchase { get; set; }
    public string KeyValue { get; set; } = string.Empty;
    public string KeyStatus { get; set; } = string.Empty;
}
public class StatusHistoryItemViewModel
{
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? Notes { get; set; }
}