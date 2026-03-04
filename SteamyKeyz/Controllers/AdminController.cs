using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;

namespace SteamyKeyz.Controllers;

// TODO: Add [Authorize(Roles = "Admin")] once auth is wired up
public class AdminController : Controller
{
    private readonly AppDbContext _context;

    public AdminController(AppDbContext context)
    {
        _context = context;
    }

    // ═══════════════════════════════════════════════════════════
    //  USER MANAGEMENT
    // ═══════════════════════════════════════════════════════════

    // GET: Admin/Users
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

    // POST: Admin/ToggleActive/5
    [HttpPost]
    [ValidateAntiForgeryToken]
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

    // POST: Admin/ChangeRole
    [HttpPost]
    [ValidateAntiForgeryToken]
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

    // ═══════════════════════════════════════════════════════════
    //  KEY MANAGEMENT
    // ═══════════════════════════════════════════════════════════

    // GET: Admin/Keys
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

        // Stats per game/platform combo
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

    // GET: Admin/AddKeys
    public async Task<IActionResult> AddKeys()
    {
        ViewBag.Games = new SelectList(
            await _context.Games.OrderBy(g => g.Title).ToListAsync(), "Id", "Title");
        ViewBag.Platforms = new SelectList(
            await _context.Platforms.OrderBy(p => p.Name).ToListAsync(), "Id", "Name");
        return View();
    }

    // POST: Admin/AddKeys
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

        // Parse: one key per line, trim whitespace, skip blanks
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

        // Check for duplicates against existing keys in DB
        var existingKeys = await _context.Keys
            .Where(k => rawKeys.Contains(k.KeyValue))
            .Select(k => k.KeyValue)
            .ToListAsync();

        var duplicatesInInput = rawKeys
            .GroupBy(k => k)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

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
}