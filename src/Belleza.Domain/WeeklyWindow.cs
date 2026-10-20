namespace Belleza.Domain;

/// <summary>Intervalo semanal local del negocio. No representa disponibilidad reservable.</summary>
public sealed class WeeklyWindow
{
    private WeeklyWindow() { }
    public WeeklyWindow(Guid businessId, DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        if (!Enum.IsDefined(day)) throw new ArgumentOutOfRangeException(nameof(day));
        if (end <= start || end - start < TimeSpan.FromMinutes(5)) throw new ArgumentException("El horario debe durar al menos 5 minutos y cerrar después de abrir.");
        Id = Guid.NewGuid(); BusinessId = businessId; Day = day; Start = start; End = end;
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public DayOfWeek Day { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly End { get; private set; }
}
