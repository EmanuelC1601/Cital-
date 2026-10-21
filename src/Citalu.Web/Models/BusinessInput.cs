using System.ComponentModel.DataAnnotations;

namespace Citalu.Web.Models;

public sealed class BusinessInput
{
    [Required, StringLength(100, MinimumLength = 2)]
    [Display(Name = "Nombre de tu negocio")]
    public string Name { get; set; } = "";
}
