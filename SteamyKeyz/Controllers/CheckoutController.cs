using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;
using SteamyKeyz.Services;
using SteamyKeyz.ViewModels;
using SteamyKeyz.Models;
using System.Text.Json;

namespace SteamyKeyz.Controllers;

public class CheckoutController : Controller
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;

    public CheckoutController(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private bool IsLoggedIn => User.Identity?.IsAuthenticated == true;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var cart = await BuildCartViewModel();

        if (cart.Items.Count == 0)
        {
            TempData["CartError"] = "Your cart is empty.";
            return RedirectToAction("Index", "Cart");
        }

        // Check stock for all items
        foreach (var item in cart.Items)
        {
            if (item.Quantity > item.AvailableStock)
            {
                TempData["CartError"] = $"Not enough stock for '{item.GameTitle}' ({item.PlatformName}). " +
                                        $"Available: {item.AvailableStock}, in cart: {item.Quantity}.";
                return RedirectToAction("Index", "Cart");
            }
        }

        var vm = new CheckoutViewModel
        {
            Cart = cart,
            IsGuest = !IsLoggedIn
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(CheckoutViewModel model)
    {
        // Validate guest fields
        if (!IsLoggedIn)
        {
            if (string.IsNullOrWhiteSpace(model.GuestName))
                ModelState.AddModelError(nameof(model.GuestName), "Name is required.");
            if (string.IsNullOrWhiteSpace(model.GuestEmail))
                ModelState.AddModelError(nameof(model.GuestEmail), "Email is required.");
            if (string.IsNullOrWhiteSpace(model.Street))
                ModelState.AddModelError(nameof(model.Street), "Street address is required.");
            if (string.IsNullOrWhiteSpace(model.PostalCode))
                ModelState.AddModelError(nameof(model.PostalCode), "Postal code is required.");
            if (string.IsNullOrWhiteSpace(model.City))
                ModelState.AddModelError(nameof(model.City), "City is required.");
            if (string.IsNullOrWhiteSpace(model.Country))
                ModelState.AddModelError(nameof(model.Country), "Country is required.");
        }

        var cart = await BuildCartViewModel();
        model.Cart = cart;
        model.IsGuest = !IsLoggedIn;

        if (cart.Items.Count == 0)
        {
            TempData["CartError"] = "Your cart is empty.";
            return RedirectToAction("Index", "Cart");
        }

        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        // ── Stock check + key reservation (inside a transaction) ──

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var invoiceItems = new List<InvoiceItem>();
            decimal totalAmount = 0;

            foreach (var cartItem in cart.Items)
            {
                for (int i = 0; i < cartItem.Quantity; i++)
                {
                    // Grab one available key
                    var key = await _context.Keys
                        .Where(k => k.GameId == cartItem.GameId
                                    && k.PlatformId == cartItem.PlatformId
                                    && k.Status == "Available")
                        .OrderBy(k => k.Id)
                        .FirstOrDefaultAsync();

                    if (key is null)
                    {
                        await transaction.RollbackAsync();
                        TempData["CartError"] =
                            $"Not enough stock for '{cartItem.GameTitle}' ({cartItem.PlatformName}). " +
                            "Please adjust your cart and try again.";
                        return RedirectToAction("Index", "Cart");
                    }

                    // Reserve the key
                    key.Status = "Reserved";

                    invoiceItems.Add(new InvoiceItem
                    {
                        KeyId = key.Id,
                        PriceAtPurchase = cartItem.UnitPrice
                    });

                    totalAmount += cartItem.UnitPrice;
                }
            }

            int userId;
            if (IsLoggedIn)
            {
                userId = CurrentUserId!.Value;
            }
            else
            {
                var guestRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Customer")
                                ?? await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");

                if (guestRole is null)
                {
                    guestRole = new Role { Name = "Customer", Description = "Default customer role" };
                    _context.Roles.Add(guestRole);
                    await _context.SaveChangesAsync();
                }

                var guestUser = new User
                {
                    Username = $"guest_{Guid.NewGuid():N}",
                    Email = model.GuestEmail!,
                    PasswordHash = string.Empty, // guest accounts can't log in
                    RoleId = guestRole.Id,
                    IsActive = false // not a real user account
                };

                _context.Users.Add(guestUser);
                await _context.SaveChangesAsync();
                userId = guestUser.Id;
            }

            var invoice = new Invoice
            {
                UserId = userId,
                InvoiceNumber = GenerateInvoiceNumber(),
                TotalAmount = totalAmount,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            foreach (var ii in invoiceItems)
            {
                invoice.InvoiceItems.Add(ii);
            }

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            // ── Clear the cart ──

            if (IsLoggedIn)
            {
                var dbCart = await _context.ShoppingCarts
                    .Include(sc => sc.CartItems)
                    .FirstOrDefaultAsync(sc => sc.UserId == CurrentUserId!.Value);

                if (dbCart is not null)
                {
                    _context.CartItems.RemoveRange(dbCart.CartItems);
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                SessionCartService.ClearCart(HttpContext.Session);
            }

            await transaction.CommitAsync();

            return RedirectToAction(nameof(Confirmation), new { invoiceId = invoice.Id });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> Confirmation(int invoiceId)
    {
        var invoice = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Game)
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Platform)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null) return NotFound();

        // Only allow the owner (or admin) to view
        if (IsLoggedIn && CurrentUserId != invoice.UserId && !User.IsInRole("Admin"))
            return Forbid();

        var vm = new OrderConfirmationViewModel
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            TotalAmount = invoice.TotalAmount,
            Status = invoice.Status,
            CreatedAt = invoice.CreatedAt,
            IsRegisteredUser = IsLoggedIn,
            Items = invoice.InvoiceItems.Select(ii => new OrderItemViewModel
            {
                GameTitle = ii.Key.Game.Title,
                PlatformName = ii.Key.Platform.Name,
                Price = ii.PriceAtPurchase,
                KeyValue = invoice.Status == "KeysSent" ? ii.Key.KeyValue : null
            }).ToList()
        };
        return View(vm);
    }

   [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SimulatePayment(int invoiceId)
{
    var invoice = await _context.Invoices
        .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Game)
        .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Platform)
        .Include(i => i.User)
        .FirstOrDefaultAsync(i => i.Id == invoiceId);

    if (invoice is null) return NotFound();

    if (invoice.Status != "Pending")
    {
        TempData["OrderError"] = "This order has already been processed.";
        return RedirectToAction(nameof(Confirmation), new { invoiceId });
    }

    // ── Mark as Paid ──
    invoice.Status = "Paid";

    _context.OrderStatusHistory.Add(new OrderStatusHistory
    {
        InvoiceId = invoice.Id,
        OldStatus = "Pending",
        NewStatus = "Paid",
        ChangedBy = User.Identity?.Name ?? "Guest",
        Notes = "Payment simulation"
    });
    foreach (var ii in invoice.InvoiceItems)
    {
        ii.Key.Status = "Sold";
    }

    // ── Queue invoice email (send immediately) ──
    var invoiceEmailModel = new InvoiceEmailModel
    {
        InvoiceNumber = invoice.InvoiceNumber,
        CustomerAddress = invoice.User.Email,
        CustomerEmail = invoice.User.Email,
        InvoiceDate = invoice.CreatedAt.ToString("dd/MM/yyyy, HH:mm"),
        Items = invoice.InvoiceItems.Select(x => new InvoiceEmailItem(x)).ToList(),
        CustomerName = invoice.User.Username,
        Subtotal = invoice.TotalAmount.ToString("C"),
        TaxAmount = (invoice.TotalAmount * 0.2M).ToString("C"),
        TotalAmount = (invoice.TotalAmount + (invoice.TotalAmount * 0.2M)).ToString("C")
    };

    _context.EmailJobs.Add(new SteamyKeyz.Models.EmailJob
    {
        InvoiceId = invoice.Id,
        EmailType = "Invoice",
        ToEmail = invoice.User.Email,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(invoiceEmailModel),
        ScheduledAt = DateTime.UtcNow // send immediately
    });

    // ── Queue keys email (10-minute delay) ──
    var isRegistered = invoice.User.IsActive && !invoice.User.Username.StartsWith("guest_");

    var keysModel = new KeysEmailModel
    {
        CustomerName = invoice.User.Username,
        InvoiceNumber = invoice.InvoiceNumber,
        IsRegisteredUser = isRegistered,
        AccountUrl = $"{Request.Scheme}://{Request.Host}/Account",
        Keys = invoice.InvoiceItems.Select(ii => new KeyEmailItem
        {
            GameTitle = ii.Key.Game.Title,
            PlatformName = ii.Key.Platform.Name,
            KeyValue = ii.Key.KeyValue
        }).ToList()
    };

    _context.EmailJobs.Add(new EmailJob
    {
        InvoiceId = invoice.Id,
        EmailType = "Keys",
        ToEmail = invoice.User.Email,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(keysModel),
        ScheduledAt = DateTime.UtcNow.AddSeconds(10) 
    });

    await _context.SaveChangesAsync();

    TempData["OrderSuccess"] = "Payment successful! Your invoice will arrive by email shortly, " +
                               "and your license keys will follow within 10 minutes.";
    return RedirectToAction(nameof(Confirmation), new { invoiceId });
}

    private async Task<CartViewModel> BuildCartViewModel()
    {
        if (IsLoggedIn)
        {
            var userId = CurrentUserId!.Value;
            var cart = await _context.ShoppingCarts
                .AsNoTracking()
                .Include(sc => sc.CartItems).ThenInclude(ci => ci.Game)
                .Include(sc => sc.CartItems).ThenInclude(ci => ci.Platform)
                .FirstOrDefaultAsync(sc => sc.UserId == userId);

            if (cart is null) return new CartViewModel();

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
        else
        {
            var sessionItems = SessionCartService.GetCart(HttpContext.Session);
            var items = new List<CartItemViewModel>();

            foreach (var si in sessionItems)
            {
                var gp = await _context.GamePlatforms
                    .AsNoTracking()
                    .Include(g => g.Game)
                    .Include(g => g.Platform)
                    .FirstOrDefaultAsync(g => g.GameId == si.GameId && g.PlatformId == si.PlatformId);

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

    private static string GenerateInvoiceNumber()
    {
        return $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
    }
}
