using System.ComponentModel.DataAnnotations;

namespace SteamyKeyz.ViewModels;

public class RegisterViewModel
{
    [Required, MaxLength(100)]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
