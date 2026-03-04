using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SteamyKeyz.ViewModels;

// ── Storefront index ────────────────────────────────────────────

public class GameIndexViewModel
{
    public List<GameCardViewModel> Games { get; set; } = new();

    public string? Search { get; set; }
    public int? PlatformId { get; set; }
    public string? SortBy { get; set; }

    public List<PlatformOptionViewModel> Platforms { get; set; } = new();

    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
}

public class GameCardViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public decimal? LowestPrice { get; set; }
    public List<string> PlatformNames { get; set; } = new();
}

public class PlatformOptionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// ── Game details ────────────────────────────────────────────────

public class GameDetailsViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Developer { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string ImageUrl { get; set; } = string.Empty;

    public List<GamePlatformPriceViewModel> Platforms { get; set; } = new();
}

public class GamePlatformPriceViewModel
{
    public int PlatformId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int AvailableKeys { get; set; }
}

// ── Admin: Create / Edit ────────────────────────────────────────

public class GameFormViewModel
{
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required, MaxLength(255)]
    public string Developer { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Publisher { get; set; } = string.Empty;

    [Display(Name = "Release Date")]
    [DataType(DataType.Date)]
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Cover image file upload. Saved to wwwroot/images/games/{Id}/cover.jpg.
    /// Optional on Edit (keeps existing file if not provided).
    /// </summary>
    [Display(Name = "Cover Image")]
    public IFormFile? CoverImage { get; set; }

    /// <summary>
    /// True when editing a game that already has a cover image on disk.
    /// </summary>
    public bool HasExistingImage { get; set; }

    /// <summary>
    /// The convention-based image URL for preview purposes (set by controller on Edit).
    /// </summary>
    public string? ExistingImageUrl { get; set; }

    public List<GamePlatformFormItem> PlatformPrices { get; set; } = new();
}

public class GamePlatformFormItem
{
    public int PlatformId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Price must be non-negative.")]
    public decimal Price { get; set; }
}