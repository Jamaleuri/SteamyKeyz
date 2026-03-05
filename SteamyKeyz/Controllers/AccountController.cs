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
    private readonly IEmailService _emailService;

    public AccountController(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
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
        return RedirectToAction("Index", "Game");
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

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Game");
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

        // Generate a secure confirmation token
        var token = GenerateEmailToken();

        var user = new User
        {
            Username = model.Username,
            Email = model.Email,
            PasswordHash = PasswordHasher.Hash(model.Password),
            RoleId = customerRole.Id,
            EmailConfirmed = false,
            EmailConfirmationToken = token,
            EmailConfirmationTokenExpiry = DateTime.UtcNow.AddHours(24)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Build the confirmation URL
        var confirmUrl = Url.Action(
            nameof(ConfirmEmail),
            "Account",
            new { userId = user.Id, token },
            Request.Scheme);

        // Send the confirmation email
        await _emailService.SendConfirmationEmailAsync(user.Email, new ConfirmationEmailModel
        {
            Username = user.Username,
            ConfirmationUrl = confirmUrl!
        });

        // Do NOT sign the user in — redirect to a "check your email" page
        return RedirectToAction(nameof(RegisterConfirmation));
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Game");

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
        if (!user.EmailConfirmed)
        {
            ModelState.AddModelError(string.Empty,
                "Please confirm your email address before logging in. Check your inbox for the confirmation link.");
            ViewBag.UnconfirmedUserId = user.Id;
            return View(model);
        }

        await SignInUser(user, user.Role.Name, model.RememberMe);

        // ── Merge guest cart into the user's DB cart ──
        await CartController.MergeSessionCartIntoDb(HttpContext, _context, user.Id);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Game");
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(int userId, string token)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            TempData["InfoMessage"] = "Invalid confirmation link.";
            return RedirectToAction("Index", "Game");
        }

        // Already confirmed?
        if (user.EmailConfirmed)
        {
            TempData["InfoMessage"] = "Your email is already confirmed. You can log in.";
            return RedirectToAction(nameof(Login));
        }

        // Token mismatch or expired?
        if (user.EmailConfirmationToken != token
            || user.EmailConfirmationTokenExpiry < DateTime.UtcNow)
        {
            ViewData["Title"] = "Link Expired";
            ViewBag.UserId = user.Id;
            return View("ConfirmEmailFailed");
        }

        // ── Confirm the email ──
        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        user.EmailConfirmationTokenExpiry = null;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Your email has been confirmed! You can now log in.";
        return RedirectToAction(nameof(Login));
    }
    [HttpGet]
    public IActionResult RegisterConfirmation()
    {
        return View();
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmation(int userId)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user is null || user.EmailConfirmed)
        {
            TempData["InfoMessage"] = "Invalid request or email already confirmed.";
            return RedirectToAction(nameof(Login));
        }

        // Generate a fresh token
        var token = GenerateEmailToken();
        user.EmailConfirmationToken = token;
        user.EmailConfirmationTokenExpiry = DateTime.UtcNow.AddHours(24);
        await _context.SaveChangesAsync();

        var confirmUrl = Url.Action(
            nameof(ConfirmEmail),
            "Account",
            new { userId = user.Id, token },
            Request.Scheme);

        await _emailService.SendConfirmationEmailAsync(user.Email, new ConfirmationEmailModel
        {
            Username = user.Username,
            ConfirmationUrl = confirmUrl!
        });

        TempData["InfoMessage"] = "A new confirmation link has been sent to your email.";
        return RedirectToAction(nameof(RegisterConfirmation));
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Game");
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
  
    private static string GenerateEmailToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
