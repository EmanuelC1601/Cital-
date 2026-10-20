namespace Belleza.Domain;

/// <summary>Representa el negocio y su espacio de datos dentro de la plataforma.</summary>
public sealed class Business
{
    private Business() { } // EF Core necesita un constructor sin parámetros.

    public Business(string ownerUserId, string name, string slug, DateTimeOffset trialStartedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) throw new ArgumentException("El propietario es obligatorio.", nameof(ownerUserId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El nombre es obligatorio.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("El enlace es obligatorio.", nameof(slug));

        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Name = name.Trim();
        Slug = slug;
        TimeZoneId = "America/Mexico_City";
        TrialStartedAtUtc = trialStartedAtUtc;
        TrialEndsAtUtc = trialStartedAtUtc.AddDays(30);
    }

    public Guid Id { get; private set; }
    public string OwnerUserId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string TimeZoneId { get; private set; } = "";
    public DateTimeOffset TrialStartedAtUtc { get; private set; }
    public DateTimeOffset TrialEndsAtUtc { get; private set; }
}
