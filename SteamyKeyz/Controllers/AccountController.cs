using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;
using SteamyKeyz.Services;
using SteamyKeyz.ViewModels;

namespace SteamyKeyz.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _context;

    public AccountController(AppDbContext context)
    {
        _context = context;
    }

    // ─── Helper: current user id from claims ─────────────────────

    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // ─── Account Page (tabbed) ───────────────────────────────────

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Index(string? tab = null)
    {
        var userId = CurrentUserId!.Value;

        var user = await _context.Users
            .AsNoTracking()
            .FirstAsync(u => u.Id == userId);

        var ownedGames = await _context.InvoiceItems
            .Include(ii => ii.Key).ThenInclude(k => k.Game)
            .Include(ii => ii.Key).ThenInclude(k => k.Platform)
            .Include(ii => ii.Invoice)
            .Where(ii => ii.Invoice.UserId == userId && ii.Invoice.Status == "KeysSent")
            .OrderByDescending(ii => ii.Invoice.CreatedAt)
            .Select(ii => new OwnedGameViewModel
            {
                Title = ii.Key.Game.Title,
                Platform = ii.Key.Platform.Name,
                ImageUrl = ii.Key.Game.ImageUrl,
                PurchasedAt = ii.Invoice.CreatedAt,
                PricePaid = ii.PriceAtPurchase
            })
            .ToListAsync();

        var vm = new AccountPageViewModel
        {
            Username = user.Username,
            Email = user.Email,
            MemberSince = user.CreatedAt,
            OwnedGames = ownedGames,
            ActiveTab = tab ?? "games"
        };

        return View(vm);
    }

    // ─── Change Password ─────────────────────────────────────────

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return await RebuildAccountPage("password");

        var user = await _context.Users.FindAsync(CurrentUserId!.Value);

        if (user is null || !PasswordHasher.Verify(model.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError("ChangePassword.CurrentPassword", "Current password is incorrect.");
            return await RebuildAccountPage("password");
        }

        user.PasswordHash = PasswordHasher.Hash(model.NewPassword);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Your password has been changed.";
        return RedirectToAction(nameof(Index), new { tab = "password" });
    }

    // ─── Delete Account ──────────────────────────────────────────

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(DeleteAccountViewModel model)
    {
        if (!ModelState.IsValid)
            return await RebuildAccountPage("delete");

        var user = await _context.Users
            .Include(u => u.ShoppingCart)
            .FirstOrDefaultAsync(u => u.Id == CurrentUserId!.Value);

        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError("DeleteAccount.Password", "Password is incorrect.");
            return await RebuildAccountPage("delete");
        }

        // Soft-delete: deactivate and wipe PII
        user.IsActive = false;
        user.Email = $"deleted_{user.Id}@removed.local";
        user.Username = $"deleted_{user.Id}";
        user.PasswordHash = string.Empty;

        if (user.ShoppingCart is not null)
            _context.ShoppingCarts.Remove(user.ShoppingCart);

        await _context.SaveChangesAsync();

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["InfoMessage"] = "Your account has been deleted.";
        return RedirectToAction("Index", "Home");
    }

    // ─── Helper: rebuild page after validation failure ───────────

    private async Task<IActionResult> RebuildAccountPage(string activeTab)
    {
        var userId = CurrentUserId!.Value;
        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        var ownedGames = await _context.InvoiceItems
            .Include(ii => ii.Key).ThenInclude(k => k.Game)
            .Include(ii => ii.Key).ThenInclude(k => k.Platform)
            .Include(ii => ii.Invoice)
            .Where(ii => ii.Invoice.UserId == userId && ii.Invoice.Status == "KeysSent")
            .OrderByDescending(ii => ii.Invoice.CreatedAt)
            .Select(ii => new OwnedGameViewModel
            {
                Title = ii.Key.Game.Title,
                Platform = ii.Key.Platform.Name,
                ImageUrl = ii.Key.Game.ImageUrl,
                PurchasedAt = ii.Invoice.CreatedAt,
                PricePaid = ii.PriceAtPurchase
            })
            .ToListAsync();

        var vm = new AccountPageViewModel
        {
            Username = user.Username,
            Email = user.Email,
            MemberSince = user.CreatedAt,
            OwnedGames = ownedGames,
            ActiveTab = activeTab
        };

        return View(nameof(Index), vm);
    }

    // ═════════════════════════════════════════════════════════════
    //  Register / Login / Logout
    // ═════════════════════════════════════════════════════════════

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var exists = await _context.Users.AnyAsync(u =>
            u.Username == model.Username || u.Email == model.Email);

        if (exists)
        {
            ModelState.AddModelError(string.Empty,
                "An account with that username or email already exists.");
            return View(model);
        }

        var customerRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "Customer");

        if (customerRole is null)
        {
            customerRole = new Role { Name = "Customer", Description = "Default customer role" };
            _context.Roles.Add(customerRole);
            await _context.SaveChangesAsync();
        }

        var user = new User
        {
            Username = model.Username,
            Email = model.Email,
            PasswordHash = PasswordHasher.Hash(model.Password),
            RoleId = customerRole.Id
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await SignInUser(user, customerRole.Name, isPersistent: false);

        // ── Merge guest cart into the new user's DB cart ──
        await CartController.MergeSessionCartIntoDb(HttpContext, _context, user.Id);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
            return View(model);

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u =>
                u.Username == model.UsernameOrEmail ||
                u.Email == model.UsernameOrEmail);

        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated.");
            return View(model);
        }

        await SignInUser(user, user.Role.Name, model.RememberMe);

        // ── Merge guest cart into the user's DB cart ──
        await CartController.MergeSessionCartIntoDb(HttpContext, _context, user.Id);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ─── Sign-in helper ──────────────────────────────────────────

    private async Task SignInUser(User user, string roleName, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, roleName)
        };

        var identity = new ClaimsIdentity(claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties
        {
            IsPersistent = isPersistent,
            ExpiresUtc = isPersistent
                ? DateTimeOffset.UtcNow.AddDays(30)
                : null
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }
}
