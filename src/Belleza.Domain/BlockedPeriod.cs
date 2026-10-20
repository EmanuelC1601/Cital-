namespace Belleza.Domain;

/// <summary>Periodo local bloqueado que se usará al calcular disponibilidad en la entrega 3.</summary>
public sealed class BlockedPeriod
{
    private BlockedPeriod() { }
    public BlockedPeriod(Guid businessId, DateOnly date, TimeOnly start, TimeOnly end, string reason)
    {
        if (end <= start || end - start < TimeSpan.FromMinutes(5)) throw new ArgumentException("El bloqueo debe durar al menos 5 minutos y terminar después de iniciar.");
        if (reason.Trim().Length > 200) throw new ArgumentException("El motivo admite hasta 200 caracteres.");
        Id = Guid.NewGuid(); BusinessId = businessId; Date = date; Start = start; End = end; Reason = reason.Trim();
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly End { get; private set; }
    public string Reason { get; private set; } = "";
}
