using System.ComponentModel.DataAnnotations;

namespace SteamyKeyz.ViewModels;

public class DeleteAccountViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm your password")]
    public string Password { get; set; } = string.Empty;
}
