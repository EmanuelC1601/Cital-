using System.ComponentModel.DataAnnotations;

namespace Belleza.Web.Models;

public sealed class RegisterInput
{
    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 12)]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña (mínimo 12 caracteres)")]
    public string Password { get; set; } = "";

    [Required, Compare(nameof(Password))]
    [DataType(DataType.Password)]
    [Display(Name = "Confirma la contraseña")]
    public string ConfirmPassword { get; set; } = "";
}
