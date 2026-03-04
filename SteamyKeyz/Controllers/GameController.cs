using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;
using SteamyKeyz.ViewModels;

namespace SteamyKeyz.Controllers;

public class GameController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private const int PageSize = 12;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const long MaxImageSize = 5 * 1024 * 1024; // 5 MB

    public GameController(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // ═════════════════════════════════════════════════════════════
    //  PUBLIC: Storefront
    // ═════════════════════════════════════════════════════════════

    public async Task<IActionResult> Index(string? search, int? platformId, string? sortBy, int page = 1)
    {
        var query = _context.Games
            .Include(g => g.GamePlatforms).ThenInclude(gp => gp.Platform)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(g =>
                g.Title.ToLower().Contains(term) ||
                g.Developer.ToLower().Contains(term) ||
                g.Publisher.ToLower().Contains(term));
        }

        if (platformId.HasValue)
        {
            query = query.Where(g =>
                g.GamePlatforms.Any(gp => gp.PlatformId == platformId.Value));
        }

        query = sortBy switch
        {
            "title_desc" => query.OrderByDescending(g => g.Title),
            "price_asc"  => query.OrderBy(g => g.GamePlatforms.Min(gp => gp.Price)),
            "price_desc" => query.OrderByDescending(g => g.GamePlatforms.Min(gp => gp.Price)),
            "newest"     => query.OrderByDescending(g => g.ReleaseDate),
            "oldest"     => query.OrderBy(g => g.ReleaseDate),
            _            => query.OrderBy(g => g.Title)
        };

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)PageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        var games = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(g => new GameCardViewModel
            {
                Id = g.Id,
                Title = g.Title,
                Developer = g.Developer,
                ImageUrl = $"/images/games/{g.Id}/cover.jpg",
                ReleaseDate = g.ReleaseDate,
                LowestPrice = g.GamePlatforms.Any()
                    ? g.GamePlatforms.Min(gp => gp.Price)
                    : null,
                PlatformNames = g.GamePlatforms
                    .Select(gp => gp.Platform.Name)
                    .ToList()
            })
            .ToListAsync();

        var platforms = await _context.Platforms
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PlatformOptionViewModel { Id = p.Id, Name = p.Name })
            .ToListAsync();

        var vm = new GameIndexViewModel
        {
            Games = games,
            Search = search,
            PlatformId = platformId,
            SortBy = sortBy,
            Platforms = platforms,
            CurrentPage = page,
            TotalPages = totalPages
        };

        return View(vm);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();

        var game = await _context.Games
            .AsNoTracking()
            .Include(g => g.GamePlatforms).ThenInclude(gp => gp.Platform)
            .Include(g => g.Keys)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game is null) return NotFound();

        var vm = new GameDetailsViewModel
        {
            Id = game.Id,
            Title = game.Title,
            Description = game.Description,
            Developer = game.Developer,
            Publisher = game.Publisher,
            ReleaseDate = game.ReleaseDate,
            ImageUrl = game.ImageUrl,
            Platforms = game.GamePlatforms.Select(gp => new GamePlatformPriceViewModel
            {
                PlatformId = gp.PlatformId,
                PlatformName = gp.Platform.Name,
                Price = gp.Price,
                AvailableKeys = game.Keys.Count(k =>
                    k.PlatformId == gp.PlatformId && k.Status == "Available")
            }).ToList()
        };

        return View(vm);
    }

    // ═════════════════════════════════════════════════════════════
    //  ADMIN: Create / Edit / Delete
    // ═════════════════════════════════════════════════════════════

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create()
    {
        var vm = new GameFormViewModel
        {
            PlatformPrices = await GetAllPlatformItems()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(GameFormViewModel model)
    {
        ValidateImage(model.CoverImage);

        if (!ModelState.IsValid)
        {
            model.PlatformPrices = await MergePlatformItems(model.PlatformPrices);
            return View(model);
        }

        var game = new Game
        {
            Title = model.Title,
            Description = model.Description,
            Developer = model.Developer,
            Publisher = model.Publisher,
            ReleaseDate = model.ReleaseDate
        };

        foreach (var pp in model.PlatformPrices.Where(p => p.IsSelected))
        {
            game.GamePlatforms.Add(new GamePlatform
            {
                PlatformId = pp.PlatformId,
                Price = pp.Price
            });
        }

        _context.Games.Add(game);
        await _context.SaveChangesAsync();

        // Save cover image to convention path now that we have the Id
        if (model.CoverImage is not null)
        {
            await SaveCoverImageAsync(model.CoverImage, game.Id);
        }

        TempData["SuccessMessage"] = $"'{game.Title}' has been created.";
        return RedirectToAction(nameof(Details), new { id = game.Id });
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();

        var game = await _context.Games
            .Include(g => g.GamePlatforms)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game is null) return NotFound();

        var allPlatforms = await GetAllPlatformItems();

        foreach (var pp in allPlatforms)
        {
            var existing = game.GamePlatforms
                .FirstOrDefault(gp => gp.PlatformId == pp.PlatformId);
            if (existing is not null)
            {
                pp.IsSelected = true;
                pp.Price = existing.Price;
            }
        }

        var vm = new GameFormViewModel
        {
            Id = game.Id,
            Title = game.Title,
            Description = game.Description,
            Developer = game.Developer,
            Publisher = game.Publisher,
            ReleaseDate = game.ReleaseDate,
            HasExistingImage = CoverImageExists(game.Id),
            ExistingImageUrl = game.ImageUrl,
            PlatformPrices = allPlatforms
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id, GameFormViewModel model)
    {
        if (id != model.Id) return NotFound();

        ValidateImage(model.CoverImage);

        if (!ModelState.IsValid)
        {
            model.HasExistingImage = CoverImageExists(id);
            model.ExistingImageUrl = $"/images/games/{id}/cover.jpg";
            model.PlatformPrices = await MergePlatformItems(model.PlatformPrices);
            return View(model);
        }

        var game = await _context.Games
            .Include(g => g.GamePlatforms)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game is null) return NotFound();

        game.Title = model.Title;
        game.Description = model.Description;
        game.Developer = model.Developer;
        game.Publisher = model.Publisher;
        game.ReleaseDate = model.ReleaseDate;

        // Replace cover image if a new one was uploaded
        if (model.CoverImage is not null)
        {
            await SaveCoverImageAsync(model.CoverImage, game.Id);
        }

        // Sync platform prices
        var selectedIds = model.PlatformPrices
            .Where(p => p.IsSelected)
            .Select(p => p.PlatformId)
            .ToHashSet();

        var toRemove = game.GamePlatforms
            .Where(gp => !selectedIds.Contains(gp.PlatformId))
            .ToList();
        _context.GamePlatforms.RemoveRange(toRemove);

        foreach (var pp in model.PlatformPrices.Where(p => p.IsSelected))
        {
            var existing = game.GamePlatforms
                .FirstOrDefault(gp => gp.PlatformId == pp.PlatformId);

            if (existing is not null)
            {
                existing.Price = pp.Price;
            }
            else
            {
                game.GamePlatforms.Add(new GamePlatform
                {
                    PlatformId = pp.PlatformId,
                    Price = pp.Price
                });
            }
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"'{game.Title}' has been updated.";
        return RedirectToAction(nameof(Details), new { id = game.Id });
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();

        var game = await _context.Games
            .AsNoTracking()
            .Include(g => g.GamePlatforms).ThenInclude(gp => gp.Platform)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game is null) return NotFound();

        return View(game);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var game = await _context.Games
            .Include(g => g.GamePlatforms)
            .Include(g => g.Keys)
            .Include(g => g.CartItems)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game is not null)
        {
            // Delete cover image folder from disk
            DeleteCoverImage(game.Id);

            _context.CartItems.RemoveRange(game.CartItems);
            _context.Keys.RemoveRange(game.Keys);
            _context.GamePlatforms.RemoveRange(game.GamePlatforms);
            _context.Games.Remove(game);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"'{game.Title}' has been deleted.";
        }

        return RedirectToAction(nameof(Index));
    }

    // ═════════════════════════════════════════════════════════════
    //  Image Helpers — convention path: wwwroot/images/games/{id}/cover.jpg
    // ═════════════════════════════════════════════════════════════

    private void ValidateImage(IFormFile? file)
    {
        if (file is null) return;

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
        {
            ModelState.AddModelError(nameof(GameFormViewModel.CoverImage),
                "Only .jpg, .jpeg, .png, and .webp files are allowed.");
        }

        if (file.Length > MaxImageSize)
        {
            ModelState.AddModelError(nameof(GameFormViewModel.CoverImage),
                "Image must be 5 MB or smaller.");
        }
    }

    /// <summary>
    /// Saves (or overwrites) the cover image to the convention path.
    /// The uploaded file is always saved as cover.jpg regardless of
    /// the original format, keeping the NotMapped property working.
    /// </summary>
    private async Task SaveCoverImageAsync(IFormFile file, int gameId)
    {
        var folder = Path.Combine(_env.WebRootPath, "images", "games", gameId.ToString());
        Directory.CreateDirectory(folder);

        var filePath = Path.Combine(folder, "cover.jpg");

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
    }

    /// <summary>
    /// Checks whether the cover image file exists on disk.
    /// </summary>
    private bool CoverImageExists(int gameId)
    {
        var filePath = Path.Combine(_env.WebRootPath, "images", "games", gameId.ToString(), "cover.jpg");
        return System.IO.File.Exists(filePath);
    }

    /// <summary>
    /// Deletes the entire image folder for a game.
    /// </summary>
    private void DeleteCoverImage(int gameId)
    {
        var folder = Path.Combine(_env.WebRootPath, "images", "games", gameId.ToString());
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Platform Helpers
    // ═════════════════════════════════════════════════════════════

    private async Task<List<GamePlatformFormItem>> GetAllPlatformItems()
    {
        return await _context.Platforms
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new GamePlatformFormItem
            {
                PlatformId = p.Id,
                PlatformName = p.Name
            })
            .ToListAsync();
    }

    private async Task<List<GamePlatformFormItem>> MergePlatformItems(
        List<GamePlatformFormItem> posted)
    {
        var all = await GetAllPlatformItems();

        foreach (var item in all)
        {
            var match = posted.FirstOrDefault(p => p.PlatformId == item.PlatformId);
            if (match is not null)
            {
                item.IsSelected = match.IsSelected;
                item.Price = match.Price;
            }
        }

        return all;
    }
}