using System.ComponentModel.DataAnnotations;

namespace Citalu.Web.Models;

public sealed class LoginInput
{
    [Required, EmailAddress]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = "";

    [Display(Name = "Mantener sesión iniciada")]
    public bool RememberMe { get; set; }
}

public sealed class EmailInput
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
}

public sealed class ResetPasswordInput
{
    [Required] public string UserId { get; set; } = "";
    [Required] public string Code { get; set; } = "";
    [Required, DataType(DataType.Password), MinLength(12)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
