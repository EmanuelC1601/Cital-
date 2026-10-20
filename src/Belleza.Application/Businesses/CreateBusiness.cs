using System.Globalization;
using System.Text;
using Belleza.Domain;

namespace Belleza.Application.Businesses;

public sealed record CreateBusinessCommand(string OwnerUserId, string Name);
public sealed record BusinessSummary(Guid Id, string Name, string Slug, DateTimeOffset TrialEndsAtUtc);

/// <summary>Valida el alta y asigna prueba y URL pública sin aceptar un identificador de negocio del navegador.</summary>
public sealed class CreateBusinessHandler(IBusinessStore store, TimeProvider clock)
{
    public async Task<BusinessSummary> HandleAsync(CreateBusinessCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        if (name.Length is < 2 or > 100) throw new ArgumentException("El nombre debe tener entre 2 y 100 caracteres.");
        if (await store.FindForOwnerAsync(command.OwnerUserId, cancellationToken) is not null)
            throw new InvalidOperationException("Esta cuenta ya tiene un negocio.");

        var baseSlug = ToSlug(name);
        var slug = baseSlug;
        for (var suffix = 2; await store.SlugExistsAsync(slug, cancellationToken); suffix++)
            slug = $"{baseSlug}-{suffix}";

        var business = new Business(command.OwnerUserId, name, slug, clock.GetUtcNow());
        await store.AddAsync(business, cancellationToken);
        return new BusinessSummary(business.Id, business.Name, business.Slug, business.TrialEndsAtUtc);
    }

    // Convierte nombres en rutas legibles; unicidad y persistencia se validan en Infrastructure.
    private static string ToSlug(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "negocio" : slug[..Math.Min(slug.Length, 60)].TrimEnd('-');
    }
}
