namespace SteamyKeyz.ViewModels;

public class OwnedGameViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime PurchasedAt { get; set; }
    public decimal PricePaid { get; set; }
}

public class AccountPageViewModel
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime MemberSince { get; set; }

    public List<OwnedGameViewModel> OwnedGames { get; set; } = new();

    public ChangePasswordViewModel ChangePassword { get; set; } = new();
    public DeleteAccountViewModel DeleteAccount { get; set; } = new();

    /// <summary>
    /// Which tab to show after a POST (defaults to "games").
    /// Values: "games", "password", "delete"
    /// </summary>
    public string ActiveTab { get; set; } = "games";
}
