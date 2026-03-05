using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;
using SteamyKeyz.Services;
using SteamyKeyz.ViewModels;

namespace SteamyKeyz.Controllers;

public class CartController : Controller
{
    private readonly AppDbContext _context;

    public CartController(AppDbContext context)
    {
        _context = context;
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private bool IsLoggedIn => User.Identity?.IsAuthenticated == true;

    // ═══════════════════════════════════════════════════════════
    //  GET: Cart/Index — display the cart
    // ═══════════════════════════════════════════════════════════

    public async Task<IActionResult> Index()
    {
        var vm = IsLoggedIn
            ? await BuildDbCartViewModel()
            : await BuildSessionCartViewModel();

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int gameId, int platformId, int quantity = 1)
    {
        // Verify the game/platform combo exists and has stock
        var gp = await _context.GamePlatforms
            .Include(gp => gp.Game)
            .Include(gp => gp.Platform)
            .FirstOrDefaultAsync(gp => gp.GameId == gameId && gp.PlatformId == platformId);

        if (gp is null)
        {
            TempData["CartError"] = "This game/platform combination doesn't exist.";
            return RedirectToAction("Index", "Game");
        }
        if (!gp.Game.IsActive)
        {
            TempData["CartError"] = "This game is currently not available.";
            return RedirectToAction("Index", "Game");
        }
        var availableKeys = await _context.Keys
            .CountAsync(k => k.GameId == gameId && k.PlatformId == platformId && k.Status == "Available");

        if (availableKeys == 0)
        {
            TempData["CartError"] = $"'{gp.Game.Title}' ({gp.Platform.Name}) is currently out of stock.";
            return RedirectToAction("Details", "Game", new { id = gameId });
        }

        if (IsLoggedIn)
        {
            await AddToDbCart(gameId, platformId, quantity);
        }
        else
        {
            SessionCartService.AddItem(HttpContext.Session, gameId, platformId, quantity);
        }

        TempData["CartSuccess"] = $"'{gp.Game.Title}' ({gp.Platform.Name}) added to cart.";
        return RedirectToAction(nameof(Index));
    }

    // ═══════════════════════════════════════════════════════════
    //  POST: Cart/Remove — remove an item entirely
    // ═══════════════════════════════════════════════════════════

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int gameId, int platformId)
    {
        if (IsLoggedIn)
        {
            var cart = await GetOrCreateDbCart();
            var item = await _context.CartItems
                .FirstOrDefaultAsync(ci => ci.ShoppingCartId == cart.Id
                                           && ci.GameId == gameId
                                           && ci.PlatformId == platformId);
            if (item is not null)
            {
                _context.CartItems.Remove(item);
                cart.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        else
        {
            SessionCartService.RemoveItem(HttpContext.Session, gameId, platformId);
        }

        return RedirectToAction(nameof(Index));
    }

    // ═══════════════════════════════════════════════════════════
    //  POST: Cart/UpdateQuantity — change quantity
    // ═══════════════════════════════════════════════════════════

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(int gameId, int platformId, int quantity)
    {
        if (quantity <= 0)
            return await Remove(gameId, platformId);

        if (IsLoggedIn)
        {
            var cart = await GetOrCreateDbCart();
            var item = await _context.CartItems
                .FirstOrDefaultAsync(ci => ci.ShoppingCartId == cart.Id
                                           && ci.GameId == gameId
                                           && ci.PlatformId == platformId);
            if (item is not null)
            {
                item.Quantity = quantity;
                cart.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        else
        {
            SessionCartService.UpdateQuantity(HttpContext.Session, gameId, platformId, quantity);
        }

        return RedirectToAction(nameof(Index));
    }

    // ═══════════════════════════════════════════════════════════
    //  Cart merge: call this after login to merge guest → DB
    // ═══════════════════════════════════════════════════════════

    public static async Task MergeSessionCartIntoDb(HttpContext httpContext, AppDbContext context, int userId)
    {
        var sessionItems = SessionCartService.GetCart(httpContext.Session);
        if (sessionItems.Count == 0) return;

        var cart = await context.ShoppingCarts
            .Include(sc => sc.CartItems)
            .FirstOrDefaultAsync(sc => sc.UserId == userId);

        if (cart is null)
        {
            cart = new ShoppingCart { UserId = userId };
            context.ShoppingCarts.Add(cart);
            await context.SaveChangesAsync();
        }

        foreach (var sessionItem in sessionItems)
        {
            var existing = cart.CartItems
                .FirstOrDefault(ci => ci.GameId == sessionItem.GameId
                                      && ci.PlatformId == sessionItem.PlatformId);

            if (existing is not null)
            {
                // Merge rule: add quantities together
                existing.Quantity += sessionItem.Quantity;
            }
            else
            {
                cart.CartItems.Add(new CartItem
                {
                    GameId = sessionItem.GameId,
                    PlatformId = sessionItem.PlatformId,
                    Quantity = sessionItem.Quantity
                });
            }
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Clear the session cart after merge
        SessionCartService.ClearCart(httpContext.Session);
    }

    // ═══════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════

    private async Task<ShoppingCart> GetOrCreateDbCart()
    {
        var userId = CurrentUserId!.Value;
        var cart = await _context.ShoppingCarts
            .Include(sc => sc.CartItems)
            .FirstOrDefaultAsync(sc => sc.UserId == userId);

        if (cart is null)
        {
            cart = new ShoppingCart { UserId = userId };
            _context.ShoppingCarts.Add(cart);
            await _context.SaveChangesAsync();
        }

        return cart;
    }

    private async Task AddToDbCart(int gameId, int platformId, int quantity)
    {
        var cart = await GetOrCreateDbCart();
        var existing = cart.CartItems
            .FirstOrDefault(ci => ci.GameId == gameId && ci.PlatformId == platformId);

        if (existing is not null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            cart.CartItems.Add(new CartItem
            {
                GameId = gameId,
                PlatformId = platformId,
                Quantity = quantity
            });
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task<CartViewModel> BuildDbCartViewModel()
    {
        var userId = CurrentUserId!.Value;
        var cart = await _context.ShoppingCarts
            .AsNoTracking()
            .Include(sc => sc.CartItems).ThenInclude(ci => ci.Game)
            .Include(sc => sc.CartItems).ThenInclude(ci => ci.Platform)
            .FirstOrDefaultAsync(sc => sc.UserId == userId);

        if (cart is null)
            return new CartViewModel();

        var items = new List<CartItemViewModel>();
        foreach (var ci in cart.CartItems)
        {
            var price = await _context.GamePlatforms
                .Where(gp => gp.GameId == ci.GameId && gp.PlatformId == ci.PlatformId)
                .Select(gp => gp.Price)
                .FirstOrDefaultAsync();

            var stock = await _context.Keys
                .CountAsync(k => k.GameId == ci.GameId && k.PlatformId == ci.PlatformId && k.Status == "Available");

            items.Add(new CartItemViewModel
            {
                GameId = ci.GameId,
                PlatformId = ci.PlatformId,
                GameTitle = ci.Game.Title,
                PlatformName = ci.Platform.Name,
                ImageUrl = $"/images/games/{ci.GameId}/cover.jpg",
                UnitPrice = price,
                Quantity = ci.Quantity,
                AvailableStock = stock
            });
        }

        return new CartViewModel { Items = items };
    }

    private async Task<CartViewModel> BuildSessionCartViewModel()
    {
        var sessionItems = SessionCartService.GetCart(HttpContext.Session);
        var items = new List<CartItemViewModel>();

        foreach (var si in sessionItems)
        {
            var gp = await _context.GamePlatforms
                .AsNoTracking()
                .Include(gp => gp.Game)
                .Include(gp => gp.Platform)
                .FirstOrDefaultAsync(gp => gp.GameId == si.GameId && gp.PlatformId == si.PlatformId);

            if (gp is null) continue;

            var stock = await _context.Keys
                .CountAsync(k => k.GameId == si.GameId && k.PlatformId == si.PlatformId && k.Status == "Available");

            items.Add(new CartItemViewModel
            {
                GameId = si.GameId,
                PlatformId = si.PlatformId,
                GameTitle = gp.Game.Title,
                PlatformName = gp.Platform.Name,
                ImageUrl = $"/images/games/{si.GameId}/cover.jpg",
                UnitPrice = gp.Price,
                Quantity = si.Quantity,
                AvailableStock = stock
            });
        }

        return new CartViewModel { Items = items };
    }
}
