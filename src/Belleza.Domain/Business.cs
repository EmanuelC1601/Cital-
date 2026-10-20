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
    public string Description { get; private set; } = "";
    public string Location { get; private set; } = "";
    public string PublicPhone { get; private set; } = "";
    public string PublicEmail { get; private set; } = "";
    public string LogoPath { get; private set; } = "";
    public string CoverPath { get; private set; } = "";
    public string PrimaryColor { get; private set; } = "#854f6c";
    public string AccentColor { get; private set; } = "#f3e9ef";

    /// <summary>Actualiza únicamente los datos que el negocio decidió hacer públicos.</summary>
    public void UpdatePublicProfile(string description, string location, string publicPhone, string publicEmail,
        string timeZoneId, string primaryColor, string accentColor)
    {
        Description = description.Trim();
        Location = location.Trim();
        PublicPhone = publicPhone.Trim();
        PublicEmail = publicEmail.Trim();
        TimeZoneId = timeZoneId;
        PrimaryColor = primaryColor;
        AccentColor = accentColor;
    }

    public void SetLogoPath(string path) => LogoPath = path;
    public void SetCoverPath(string path) => CoverPath = path;
}
