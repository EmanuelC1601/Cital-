namespace Citalu.Domain;

/// <summary>Una cita conserva el acuerdo original aunque el catálogo cambie después.</summary>
public sealed class Appointment
{
    private Appointment() { }
    public Appointment(Guid businessId, Guid serviceId, string customerName, string customerPhone,
        string customerEmail, DateTimeOffset startUtc, decimal price, int durationMinutes, int bufferMinutes,
        string serviceName, string manageTokenHash, string manageTokenCiphertext,
        DateTimeOffset tokenExpiresAtUtc, string idempotencyKeyHash)
    {
        if (string.IsNullOrWhiteSpace(customerName) || customerName.Trim().Length > 120) throw new ArgumentException("Escribe tu nombre.");
        if (string.IsNullOrWhiteSpace(customerPhone) || customerPhone.Trim().Length > 40) throw new ArgumentException("Escribe un teléfono de contacto.");
        if (price < 0 || decimal.Round(price, 2) != price || durationMinutes is < 5 or > 480 || bufferMinutes is < 0 or > 180) throw new ArgumentException("El servicio no es válido.");
        Id = Guid.NewGuid(); BusinessId = businessId; ServiceId = serviceId; CustomerName = customerName.Trim();
        CustomerPhone = customerPhone.Trim(); CustomerEmail = customerEmail.Trim(); StartUtc = startUtc.ToUniversalTime();
        PriceSnapshot = price; DurationMinutesSnapshot = durationMinutes; BufferMinutesSnapshot = bufferMinutes;
        ServiceNameSnapshot = serviceName; Status = AppointmentStatus.Confirmed; ManageTokenHash = manageTokenHash;
        ManageTokenCiphertext = manageTokenCiphertext;
        TokenExpiresAtUtc = tokenExpiresAtUtc.ToUniversalTime(); CreatedAtUtc = DateTimeOffset.UtcNow;
        IdempotencyKeyHash = idempotencyKeyHash;
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string CustomerName { get; private set; } = "";
    public string CustomerPhone { get; private set; } = "";
    public string CustomerEmail { get; private set; } = "";
    public string ServiceNameSnapshot { get; private set; } = "";
    public decimal PriceSnapshot { get; private set; }
    public int DurationMinutesSnapshot { get; private set; }
    public int BufferMinutesSnapshot { get; private set; }
    public DateTimeOffset StartUtc { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string ManageTokenHash { get; private set; } = "";
    /// <summary>Token protegido con ASP.NET Data Protection para responder de forma estable a reintentos.</summary>
    public string ManageTokenCiphertext { get; private set; } = "";
    public DateTimeOffset TokenExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string IdempotencyKeyHash { get; private set; } = "";
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset OccupiedUntilUtc => StartUtc.AddMinutes(DurationMinutesSnapshot + BufferMinutesSnapshot);

    public void Reschedule(DateTimeOffset startUtc, string newTokenHash, string newTokenCiphertext,
        DateTimeOffset tokenExpiresAtUtc)
    {
        EnsureActive(); StartUtc = startUtc.ToUniversalTime(); ManageTokenHash = newTokenHash;
        ManageTokenCiphertext = newTokenCiphertext;
        TokenExpiresAtUtc = tokenExpiresAtUtc.ToUniversalTime(); UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
    /// <summary>Rota el acceso vencido sin alterar el horario confirmado.</summary>
    public void RotateManageToken(string newTokenHash, string newTokenCiphertext, DateTimeOffset tokenExpiresAtUtc)
    {
        EnsureActive(); ManageTokenHash = newTokenHash; ManageTokenCiphertext = newTokenCiphertext;
        TokenExpiresAtUtc = tokenExpiresAtUtc.ToUniversalTime();
    }
    public void Cancel() { EnsureActive(); Status = AppointmentStatus.Cancelled; UpdatedAtUtc = DateTimeOffset.UtcNow; }
    public void Complete(bool noShow)
    {
        EnsureActive(); Status = noShow ? AppointmentStatus.NoShow : AppointmentStatus.Completed; UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
    private void EnsureActive()
    {
        if (Status != AppointmentStatus.Confirmed) throw new InvalidOperationException("La cita ya no está activa.");
    }
}

public enum AppointmentStatus { Confirmed = 0, Cancelled = 1, Completed = 2, NoShow = 3 }
