namespace Citalu.Domain;

/// <summary>Historial inmutable del ciclo de vida y los cambios de horario de una cita.</summary>
public sealed class AppointmentChange
{
    private AppointmentChange() { }
    public AppointmentChange(Guid appointmentId, Guid businessId, string changeType, string actor,
        DateTimeOffset? oldStartUtc, DateTimeOffset? newStartUtc, string? note = null)
    {
        Id = Guid.NewGuid(); AppointmentId = appointmentId; BusinessId = businessId; ChangeType = changeType;
        Actor = actor; OldStartUtc = oldStartUtc?.ToUniversalTime(); NewStartUtc = newStartUtc?.ToUniversalTime();
        Note = note ?? ""; CreatedAtUtc = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid BusinessId { get; private set; }
    public string ChangeType { get; private set; } = "";
    public string Actor { get; private set; } = "";
    public DateTimeOffset? OldStartUtc { get; private set; }
    public DateTimeOffset? NewStartUtc { get; private set; }
    public string Note { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
