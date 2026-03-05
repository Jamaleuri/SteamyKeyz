namespace SteamyKeyz.ViewModels;

public class AdminDashboardViewModel
{
    // Orders
    public int TotalOrders { get; set; }
    public int PendingOrders { get; set; }
    public int PaidOrders { get; set; }
    public int InvoiceSentOrders { get; set; }
    public int KeysSentOrders { get; set; }
    public decimal TotalRevenue { get; set; }

    // Games & Keys
    public int TotalGames { get; set; }
    public int ActiveGames { get; set; }
    public int DeactivatedGames { get; set; }
    public int AvailableKeys { get; set; }
    public int ReservedKeys { get; set; }
    public int SoldKeys { get; set; }

    // Users
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int SuspendedUsers { get; set; }

    // Email Jobs
    public int PendingEmailJobs { get; set; }
    public int FailedEmailJobs { get; set; }

    // Recent orders
    public List<AdminOrderSummaryViewModel> RecentOrders { get; set; } = new();
}