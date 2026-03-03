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

    // ─── Register ────────────────────────────────────────────────

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

        // Check for duplicate username or email (prevent enumeration by
        // using a single generic message)
        var exists = await _context.Users.AnyAsync(u =>
            u.Username == model.Username || u.Email == model.Email);

        if (exists)
        {
            ModelState.AddModelError(string.Empty,
                "An account with that username or email already exists.");
            return View(model);
        }

        // Ensure a default "Customer" role exists
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

        // Auto-sign-in after registration
        await SignInUser(user, customerRole.Name, isPersistent: false);

        return RedirectToAction("Index", "Home");
    }

    // ─── Login ───────────────────────────────────────────────────

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

        // Find user by username OR email
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u =>
                u.Username == model.UsernameOrEmail ||
                u.Email == model.UsernameOrEmail);

        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            // Generic message to prevent user enumeration
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated.");
            return View(model);
        }

        await SignInUser(user, user.Role.Name, model.RememberMe);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    // ─── Logout ──────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // ─── Access Denied ───────────────────────────────────────────

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // ─── Helpers ─────────────────────────────────────────────────

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
                : null                                    // session cookie
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }
}
