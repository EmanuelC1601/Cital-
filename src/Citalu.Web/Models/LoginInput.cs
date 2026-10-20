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
