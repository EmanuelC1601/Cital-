namespace Belleza.Domain;

/// <summary>Descanso dentro de un intervalo semanal del negocio.</summary>
public sealed class WeeklyBreak
{
    private WeeklyBreak() { }
    public WeeklyBreak(Guid businessId, DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        if (!Enum.IsDefined(day)) throw new ArgumentOutOfRangeException(nameof(day));
        if (end <= start || end - start < TimeSpan.FromMinutes(5)) throw new ArgumentException("El descanso debe durar al menos 5 minutos y terminar después de iniciar.");
        Id = Guid.NewGuid(); BusinessId = businessId; Day = day; Start = start; End = end;
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public DayOfWeek Day { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly End { get; private set; }
}
