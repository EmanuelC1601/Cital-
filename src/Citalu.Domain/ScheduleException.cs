namespace Citalu.Domain;

/// <summary>Fecha especial: cierre completo o un horario local distinto al habitual.</summary>
public sealed class ScheduleException
{
    private ScheduleException() { }
    public ScheduleException(Guid businessId, DateOnly date, bool isClosed, TimeOnly? start, TimeOnly? end)
    {
        if (!isClosed && (start is null || end is null || end <= start || end - start < TimeSpan.FromMinutes(5))) throw new ArgumentException("Indica un intervalo de al menos 5 minutos para la fecha especial.");
        Id = Guid.NewGuid(); BusinessId = businessId; Date = date; IsClosed = isClosed; Start = isClosed ? null : start; End = isClosed ? null : end;
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public DateOnly Date { get; private set; }
    public bool IsClosed { get; private set; }
    public TimeOnly? Start { get; private set; }
    public TimeOnly? End { get; private set; }

    public void Update(bool isClosed, TimeOnly? start, TimeOnly? end)
    {
        if (!isClosed && (start is null || end is null || end <= start || end - start < TimeSpan.FromMinutes(5))) throw new ArgumentException("Indica un intervalo de al menos 5 minutos para la fecha especial.");
        IsClosed = isClosed; Start = isClosed ? null : start; End = isClosed ? null : end;
    }
}
