using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Citalu.Web.Services;

/// <summary>Convierte reglas locales del negocio en inicios válidos; el almacenamiento de citas sigue en UTC.</summary>
public sealed class AvailabilityCalculator(ApplicationDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<TimeOnly>> GetSlotsAsync(Business business, Service service, DateOnly date,
        Guid? excludeAppointmentId, CancellationToken cancellationToken)
    {
        // Las reglas son horas locales del negocio; los límites de anticipación y las citas se comparan en UTC.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var now = clock.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        if (date < localDate || date > localDate.AddDays(business.MaximumBookingDays)) return [];

        var exception = await db.ScheduleExceptions.SingleOrDefaultAsync(x => x.BusinessId == business.Id && x.Date == date, cancellationToken);
        if (exception?.IsClosed == true) return [];
        var windows = exception is null
            ? await db.WeeklyWindows.Where(x => x.BusinessId == business.Id && x.Day == date.DayOfWeek)
                .Select(x => new LocalWindow(x.Start, x.End)).ToListAsync(cancellationToken)
            : [new LocalWindow(exception.Start!.Value, exception.End!.Value)];
        if (windows.Count == 0) return [];

        var breaks = await db.WeeklyBreaks.Where(x => x.BusinessId == business.Id && x.Day == date.DayOfWeek)
            .Select(x => new LocalWindow(x.Start, x.End)).ToListAsync(cancellationToken);
        var blocks = await db.BlockedPeriods.Where(x => x.BusinessId == business.Id && x.Date == date)
            .Select(x => new LocalWindow(x.Start, x.End)).ToListAsync(cancellationToken);
        var appointments = await db.Appointments.Where(x => x.BusinessId == business.Id && x.Status == AppointmentStatus.Confirmed
                && x.StartUtc < date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc).AddDays(1)
                && x.StartUtc >= date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1)
                && (!excludeAppointmentId.HasValue || x.Id != excludeAppointmentId.Value))
            .ToListAsync(cancellationToken);
        var results = new List<TimeOnly>();
        // El margen forma parte del espacio ocupado, aunque no cambie la duración visible de la cita.
        var occupiedMinutes = service.DurationMinutes + service.BufferMinutes;
        var earliestUtc = now.AddMinutes(business.MinimumAdvanceMinutes);

        foreach (var window in windows)
        {
            for (var time = window.Start; time.AddMinutes(occupiedMinutes) <= window.End; time = time.AddMinutes(15))
            {
                var end = time.AddMinutes(occupiedMinutes);
                if (breaks.Any(p => p.Start < end && time < p.End) || blocks.Any(p => p.Start < end && time < p.End)) continue;
                var local = date.ToDateTime(time, DateTimeKind.Unspecified);
                // Se omiten horas locales inexistentes o repetidas durante cambios de horario estacional.
                if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) continue;
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
                if (startUtc < earliestUtc.UtcDateTime) continue;
                var occupiedUntil = startUtc.AddMinutes(occupiedMinutes);
                if (appointments.Any(a => a.StartUtc.UtcDateTime < occupiedUntil && startUtc < a.OccupiedUntilUtc.UtcDateTime)) continue;
                results.Add(time);
            }
        }
        return results.Distinct().Order().ToArray();
    }
    private sealed record LocalWindow(TimeOnly Start, TimeOnly End);
}
