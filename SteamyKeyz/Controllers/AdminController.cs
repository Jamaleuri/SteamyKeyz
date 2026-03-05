using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;
using SteamyKeyz.Services;
using SteamyKeyz.ViewModels;

namespace SteamyKeyz.Controllers;

[Authorize(Policy = "Staff")]
public class AdminController : Controller
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;

    public AdminController(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    // ═══════════════════════════════════════════════════════════
//  DASHBOARD
// ═══════════════════════════════════════════════════════════

public async Task<IActionResult> Dashboard()
{
    var invoices = _context.Invoices.AsNoTracking();
    var keys = _context.Keys.AsNoTracking();
    var users = _context.Users.AsNoTracking();
    var games = _context.Games.AsNoTracking();
    var emailJobs = _context.EmailJobs.AsNoTracking();

    var vm = new AdminDashboardViewModel
    {
        // Orders
        TotalOrders = await invoices.CountAsync(),
        PendingOrders = await invoices.CountAsync(i => i.Status == "Pending"),
        PaidOrders = await invoices.CountAsync(i => i.Status == "Paid"),
        InvoiceSentOrders = await invoices.CountAsync(i => i.Status == "InvoiceSent"),
        KeysSentOrders = await invoices.CountAsync(i => i.Status == "KeysSent"),
        TotalRevenue = await invoices
            .Where(i => i.Status == "Paid" || i.Status == "InvoiceSent" || i.Status ==  "KeysSent")
            .SumAsync(i => (decimal?)i.TotalAmount) ?? 0,

        // Games & Keys
        TotalGames = await games.CountAsync(),
        ActiveGames = await games.CountAsync(g => g.IsActive),
        DeactivatedGames = await games.CountAsync(g => !g.IsActive),
        AvailableKeys = await keys.CountAsync(k => k.Status == "Available"),
        ReservedKeys = await keys.CountAsync(k => k.Status == "Reserved"),
        SoldKeys = await keys.CountAsync(k => k.Status == "Sold"),

        // Users
        TotalUsers = await users.CountAsync(),
        ActiveUsers = await users.CountAsync(u => u.IsActive),
        SuspendedUsers = await users.CountAsync(u => !u.IsActive),

        // Email Jobs
        PendingEmailJobs = await emailJobs.CountAsync(j => j.Status == "Pending"),
        FailedEmailJobs = await emailJobs.CountAsync(j => j.Status == "Failed"),

        // Recent orders (last 5)
        RecentOrders = await invoices
            .Include(i => i.User)
            .Include(i => i.InvoiceItems)
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new AdminOrderSummaryViewModel
            {
                InvoiceId = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                Username = i.User.Username,
                Email = i.User.Email,
                IsGuestOrder = !i.User.IsActive && i.User.Username.StartsWith("guest_"),
                TotalAmount = i.TotalAmount,
                Status = i.Status,
                CreatedAt = i.CreatedAt,
                ItemCount = i.InvoiceItems.Count
            })
            .ToListAsync()
    };

    return View(vm);
}
    // ═══════════════════════════════════════════════════════════
    //  USER MANAGEMENT
    // ═══════════════════════════════════════════════════════════

    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Users(string? search, int? roleId, bool? isActive)
    {
        var query = _context.Users
            .Include(u => u.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term));
        }

        if (roleId.HasValue)
            query = query.Where(u => u.RoleId == roleId.Value);

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var users = await query.OrderBy(u => u.Username).ToListAsync();
        var roles = await _context.Roles.OrderBy(r => r.Name).ToListAsync();

        ViewBag.Roles = roles;
        ViewBag.Search = search;
        ViewBag.RoleId = roleId;
        ViewBag.IsActive = isActive;

        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        await _context.SaveChangesAsync();

        TempData["Success"] = user.IsActive
            ? $"User \"{user.Username}\" has been activated."
            : $"User \"{user.Username}\" has been suspended.";

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ChangeRole(int userId, int roleId)
    {
        var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound();

        var role = await _context.Roles.FindAsync(roleId);
        if (role == null) return BadRequest("Role not found.");

        var oldRole = user.Role.Name;
        user.RoleId = roleId;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"User \"{user.Username}\" role changed from {oldRole} to {role.Name}.";
        return RedirectToAction(nameof(Users));
    }

    public async Task<IActionResult> Keys(int? gameId, int? platformId, string? status)
    {
        var query = _context.Keys
            .Include(k => k.Game)
            .Include(k => k.Platform)
            .AsQueryable();

        if (gameId.HasValue)
            query = query.Where(k => k.GameId == gameId.Value);

        if (platformId.HasValue)
            query = query.Where(k => k.PlatformId == platformId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(k => k.Status == status);

        var keys = await query.OrderByDescending(k => k.AddedAt).Take(200).ToListAsync();

        ViewBag.Games = new SelectList(
            await _context.Games.OrderBy(g => g.Title).ToListAsync(), "Id", "Title", gameId);
        ViewBag.Platforms = new SelectList(
            await _context.Platforms.OrderBy(p => p.Name).ToListAsync(), "Id", "Name", platformId);
        ViewBag.SelectedGameId = gameId;
        ViewBag.SelectedPlatformId = platformId;
        ViewBag.SelectedStatus = status;

        var stats = await _context.Keys
            .GroupBy(k => new { k.GameId, k.PlatformId, k.Status })
            .Select(g => new
            {
                g.Key.GameId,
                g.Key.PlatformId,
                g.Key.Status,
                Count = g.Count()
            })
            .ToListAsync();

        ViewBag.KeyStats = stats;

        return View(keys);
    }

    public async Task<IActionResult> AddKeys()
    {
        ViewBag.Games = new SelectList(
            await _context.Games.OrderBy(g => g.Title).ToListAsync(), "Id", "Title");
        ViewBag.Platforms = new SelectList(
            await _context.Platforms.OrderBy(p => p.Name).ToListAsync(), "Id", "Name");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddKeys(int gameId, int platformId, string keysText)
    {
        var game = await _context.Games.FindAsync(gameId);
        var platform = await _context.Platforms.FindAsync(platformId);

        if (game == null || platform == null)
        {
            TempData["Error"] = "Invalid game or platform selection.";
            return RedirectToAction(nameof(AddKeys));
        }

        if (string.IsNullOrWhiteSpace(keysText))
        {
            TempData["Error"] = "Please enter at least one key.";
            return RedirectToAction(nameof(AddKeys));
        }

        var rawKeys = keysText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .ToList();

        if (rawKeys.Count == 0)
        {
            TempData["Error"] = "No valid keys found in input.";
            return RedirectToAction(nameof(AddKeys));
        }

        var existingKeys = await _context.Keys
            .Where(k => rawKeys.Contains(k.KeyValue))
            .Select(k => k.KeyValue)
            .ToListAsync();

        var newKeys = rawKeys
            .Distinct()
            .Where(k => !existingKeys.Contains(k))
            .ToList();

        int skippedDuplicates = rawKeys.Count - newKeys.Count;

        foreach (var keyValue in newKeys)
        {
            _context.Keys.Add(new Key
            {
                GameId = gameId,
                PlatformId = platformId,
                KeyValue = keyValue,
                Status = "Available",
                AddedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var msg = $"{newKeys.Count} key(s) added for \"{game.Title}\" ({platform.Name}).";
        if (skippedDuplicates > 0)
            msg += $" {skippedDuplicates} duplicate(s) skipped.";

        TempData["Success"] = msg;
        return RedirectToAction(nameof(Keys), new { gameId, platformId });
    }

    // GET: Admin/Orders
    public async Task<IActionResult> Orders(string? status, string? search, DateTime? dateFrom, DateTime? dateTo)
    {
        var query = _context.Invoices
            .Include(i => i.User)
            .Include(i => i.InvoiceItems)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(i =>
                i.InvoiceNumber.ToLower().Contains(term) ||
                i.User.Username.ToLower().Contains(term) ||
                i.User.Email.ToLower().Contains(term));
        }

        if (dateFrom.HasValue)
            query = query.Where(i => i.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(i => i.CreatedAt <= dateTo.Value.AddDays(1));

        var invoices = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(200)
            .ToListAsync();

        // Stats across all orders (unfiltered)
        var allInvoices = _context.Invoices.AsNoTracking();

        var vm = new AdminOrderIndexViewModel
        {
            Orders = invoices.Select(i => new AdminOrderSummaryViewModel
            {
                InvoiceId = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                Username = i.User.Username,
                Email = i.User.Email,
                IsGuestOrder = !i.User.IsActive && i.User.Username.StartsWith("guest_"),
                TotalAmount = i.TotalAmount,
                Status = i.Status,
                CreatedAt = i.CreatedAt,
                ItemCount = i.InvoiceItems.Count
            }).ToList(),

            Status = status,
            Search = search,
            DateFrom = dateFrom,
            DateTo = dateTo,

            TotalOrders = await allInvoices.CountAsync(),
            PendingCount = await allInvoices.CountAsync(i => i.Status == "Pending"),
            PaidCount = await allInvoices.CountAsync(i => i.Status == "Paid"),
            KeysSentCount = await allInvoices.CountAsync(i => i.Status == "KeysSent"),
            TotalRevenue = await allInvoices
                .Where(i => i.Status == "Paid" || i.Status == "InvoiceSent" || i.Status == "KeysSent")
                .SumAsync(i => i.TotalAmount)
        };

        return View(vm);
    }

    // GET: Admin/OrderDetail/5
    public async Task<IActionResult> OrderDetail(int id)
    {
        var invoice = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.User)
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Game)
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Platform)
            .Include(i => i.StatusHistory)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice is null) return NotFound();

        var vm = new AdminOrderDetailViewModel
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            TotalAmount = invoice.TotalAmount,
            Status = invoice.Status,
            CreatedAt = invoice.CreatedAt,
            UserId = invoice.UserId,
            Username = invoice.User.Username,
            Email = invoice.User.Email,
            IsGuestOrder = !invoice.User.IsActive && invoice.User.Username.StartsWith("guest_"),
            Items = invoice.InvoiceItems.Select(ii => new AdminOrderItemViewModel
            {
                KeyId = ii.Key.Id,
                GameTitle = ii.Key.Game.Title,
                PlatformName = ii.Key.Platform.Name,
                PriceAtPurchase = ii.PriceAtPurchase,
                KeyValue = ii.Key.KeyValue,
                KeyStatus = ii.Key.Status
            }).ToList(),
            StatusHistory = invoice.StatusHistory
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new StatusHistoryItemViewModel
            {
            OldStatus = h.OldStatus,
            NewStatus = h.NewStatus,
            ChangedBy = h.ChangedBy,
            ChangedAt = h.ChangedAt,
            Notes = h.Notes
        })
        .ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOrderStatus(int invoiceId, string newStatus)
    {
        var validStatuses = new[] { "Pending", "Paid", "InvoiceSent", "KeysSent" };
        if (!validStatuses.Contains(newStatus))
            return BadRequest("Invalid status.");

        var invoice = await _context.Invoices
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null) return NotFound();

        var oldStatus = invoice.Status;

        if (oldStatus == newStatus)
        {
            TempData["Success"] = "Status unchanged.";
            return RedirectToAction(nameof(OrderDetail), new { id = invoiceId });
        }

        invoice.Status = newStatus;

        // Key status adjustments
        if (newStatus is "Paid" or "InvoiceSent" or "KeysSent")
        {
            foreach (var ii in invoice.InvoiceItems)
            {
                if (ii.Key.Status == "Reserved")
                    ii.Key.Status = "Sold";
            }
        }

        if (newStatus == "Pending")
        {
            foreach (var ii in invoice.InvoiceItems)
            {
                if (ii.Key.Status is "Reserved" or "Sold")
                    ii.Key.Status = "Available";
            }
        }

        // ── Log the status change ──
        _context.OrderStatusHistory.Add(new OrderStatusHistory
        {
            InvoiceId = invoiceId,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ChangedBy = User.Identity?.Name ?? "Unknown",
            Notes = $"Manual status change by {User.Identity?.Name}"
        });

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Order {invoice.InvoiceNumber} status changed from {oldStatus} to {newStatus}.";
        return RedirectToAction(nameof(OrderDetail), new { id = invoiceId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleGameActive(int id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game is null) return NotFound();

        game.IsActive = !game.IsActive;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = game.IsActive
            ? $"'{game.Title}' is now active and visible in the store."
            : $"'{game.Title}' has been deactivated and hidden from the store.";

        return RedirectToAction("Details", "Game", new { id });
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendInvoice(int invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.User)
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Game)
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Platform)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null) return NotFound();

        var model = new InvoiceEmailModel
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

        try
        {
            await _emailService.SendInvoiceEmailAsync(invoice.User.Email, model);
            TempData["Success"] = $"Invoice {invoice.InvoiceNumber} resent to {invoice.User.Email}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to resend invoice: {ex.Message}";
        }

        return RedirectToAction(nameof(OrderDetail), new { id = invoiceId });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendKeys(int invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.User)
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Game)
            .Include(i => i.InvoiceItems).ThenInclude(ii => ii.Key).ThenInclude(k => k.Platform)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice is null) return NotFound();

        var isRegistered = invoice.User.IsActive && !invoice.User.Username.StartsWith("guest_");

        var model = new KeysEmailModel
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

        try
        {
            await _emailService.SendKeysEmailAsync(invoice.User.Email, model);
            TempData["Success"] = $"Keys for order {invoice.InvoiceNumber} resent to {invoice.User.Email}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to resend keys: {ex.Message}";
        }

        return RedirectToAction(nameof(OrderDetail), new { id = invoiceId });
    }
}
