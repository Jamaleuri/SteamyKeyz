
namespace SteamyKeyz.Services;

public interface IEmailService
{
    Task SendConfirmationEmailAsync(string toEmail, ConfirmationEmailModel model);
    Task SendEmailAsync(string toEmail, string subject, string htmlBody);
    Task SendInvoiceEmailAsync(string toEmail, InvoiceEmailModel model);
    Task SendKeysEmailAsync(string toEmail, KeysEmailModel model);
}

// ── Models that carry the data for each email type ──────────────

public class InvoiceEmailModel
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceDate { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public List<InvoiceEmailItem> Items { get; set; } = new();
    public string Subtotal { get; set; } = "0.00";
    public string TaxAmount { get; set; } = "0.00";
    public string TotalAmount { get; set; } = "0.00";
}

public class InvoiceEmailItem
{
   
    public InvoiceEmailItem(InvoiceItem invoiceItem)
    {
        GameTitle = invoiceItem.Key.Game.Title;
        PlatformName = invoiceItem.Key.Platform.Name;
        Price = invoiceItem.PriceAtPurchase.ToString();
    }
    public string GameTitle { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public string Price { get; set; } = "0.00";
}

public class KeysEmailModel
{
    public string CustomerName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public bool IsRegisteredUser { get; set; }
    public string AccountUrl { get; set; } = string.Empty;
    public List<KeyEmailItem> Keys { get; set; } = new();
}

public class KeyEmailItem
{
    public string GameTitle { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public string KeyValue { get; set; } = string.Empty;
}
public class ConfirmationEmailModel
{
    public string Username { get; set; } = string.Empty;
    public string ConfirmationUrl { get; set; } = string.Empty;
}