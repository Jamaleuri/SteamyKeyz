using System.Text.Json;

namespace SteamyKeyz.Services;

/// <summary>
/// Lightweight session-based cart for guests (not logged in).
/// Stores a list of (GameId, PlatformId, Quantity) tuples in the session.
/// </summary>
public class SessionCartService
{
    private const string SessionKey = "GuestCart";

    public static List<SessionCartItem> GetCart(ISession session)
    {
        var json = session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json))
            return new List<SessionCartItem>();

        return JsonSerializer.Deserialize<List<SessionCartItem>>(json) ?? new();
    }

    public static void SaveCart(ISession session, List<SessionCartItem> cart)
    {
        var json = JsonSerializer.Serialize(cart);
        session.SetString(SessionKey, json);
    }

    public static void AddItem(ISession session, int gameId, int platformId, int quantity = 1)
    {
        var cart = GetCart(session);
        var existing = cart.FirstOrDefault(c => c.GameId == gameId && c.PlatformId == platformId);

        if (existing is not null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            cart.Add(new SessionCartItem
            {
                GameId = gameId,
                PlatformId = platformId,
                Quantity = quantity
            });
        }

        SaveCart(session, cart);
    }

    public static void RemoveItem(ISession session, int gameId, int platformId)
    {
        var cart = GetCart(session);
        cart.RemoveAll(c => c.GameId == gameId && c.PlatformId == platformId);
        SaveCart(session, cart);
    }

    public static void UpdateQuantity(ISession session, int gameId, int platformId, int quantity)
    {
        var cart = GetCart(session);
        var existing = cart.FirstOrDefault(c => c.GameId == gameId && c.PlatformId == platformId);

        if (existing is not null)
        {
            if (quantity <= 0)
                cart.Remove(existing);
            else
                existing.Quantity = quantity;
        }

        SaveCart(session, cart);
    }

    public static void ClearCart(ISession session)
    {
        session.Remove(SessionKey);
    }
}

public class SessionCartItem
{
    public int GameId { get; set; }
    public int PlatformId { get; set; }
    public int Quantity { get; set; }
}
