namespace Citalu.Web.Models;

/// <summary>
/// Describes an approved Citalú brand asset used by the shared Razor partial.
/// </summary>
public sealed record BrandLogoViewModel(string AssetFile, string CssClass, bool Decorative = true);
