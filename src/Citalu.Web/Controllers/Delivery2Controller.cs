using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Citalu.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Citalu.Web.Controllers;

[Authorize, Route("negocio")]
public sealed class Delivery2Controller(ApplicationDbContext db, UserManager<IdentityUser> users,
    IWebHostEnvironment environment) : Controller
{
    // Cada pantalla de administración resuelve primero el negocio del usuario autenticado.
    private Task<Business?> OwnBusinessAsync(CancellationToken token) =>
        db.Businesses.SingleOrDefaultAsync(item => item.OwnerUserId == users.GetUserId(User), token);

    // Serializa cambios de catálogo/calendario con reservas y altas manuales del mismo negocio.
    private async Task LockBusinessForWriteAsync(Guid id, CancellationToken token) =>
        _ = await db.Businesses.FromSqlInterpolated($"SELECT * FROM dbo.Businesses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            .AsNoTracking().SingleAsync(token);

    private async Task<bool> AppointmentOverlapsLocalIntervalAsync(Business business, DateOnly? date, DayOfWeek? day,
        TimeOnly start, TimeOnly end, CancellationToken token)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Appointments.Where(x => x.BusinessId == business.Id && x.Status == AppointmentStatus.Confirmed
            && x.StartUtc.AddMinutes(x.DurationMinutesSnapshot + x.BufferMinutesSnapshot) >= now).ToListAsync(token);
        return rows.Any(appointment =>
        {
            if (appointment.OccupiedUntilUtc <= now) return false;
            var localStart = TimeZoneInfo.ConvertTime(appointment.StartUtc, zone);
            var localEnd = TimeZoneInfo.ConvertTime(appointment.OccupiedUntilUtc, zone);
            var localDate = DateOnly.FromDateTime(localStart.DateTime);
            if (date.HasValue && localDate != date.Value || day.HasValue && localDate.DayOfWeek != day.Value) return false;
            var appointmentStart = TimeOnly.FromDateTime(localStart.DateTime);
            var appointmentEnd = TimeOnly.FromDateTime(localEnd.DateTime);
            return appointmentStart < end && start < appointmentEnd;
        });
    }

    private async Task<bool> AppointmentOutsideDateWindowAsync(Business business, DateOnly date,
        TimeOnly start, TimeOnly end, bool closed, CancellationToken token)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Appointments.Where(x => x.BusinessId == business.Id && x.Status == AppointmentStatus.Confirmed
            && x.StartUtc.AddMinutes(x.DurationMinutesSnapshot + x.BufferMinutesSnapshot) >= now).ToListAsync(token);
        return rows.Any(appointment =>
        {
            if (appointment.OccupiedUntilUtc <= now) return false;
            var localStart = TimeZoneInfo.ConvertTime(appointment.StartUtc, zone);
            if (DateOnly.FromDateTime(localStart.DateTime) != date) return false;
            if (closed) return true;
            var localEnd = TimeZoneInfo.ConvertTime(appointment.OccupiedUntilUtc, zone);
            return TimeOnly.FromDateTime(localStart.DateTime) < start || TimeOnly.FromDateTime(localEnd.DateTime) > end;
        });
    }

    [HttpGet("servicios")]
    public async Task<IActionResult> Services(CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        var items = await db.Services.Where(item => item.BusinessId == business.Id).OrderBy(item => item.Name).ToListAsync(token);
        return View(new ServiceListPage(business.Name, items, new ServiceInput()));
    }

    [HttpPost("servicios/guardar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveService(ServiceInput input, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        if (!ModelState.IsValid) return View("Services", new ServiceListPage(business.Name,
            await db.Services.Where(service => service.BusinessId == business.Id).OrderBy(service => service.Name).ToListAsync(token), input));

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        try
        {
            Service? item = input.Id is null ? null : await db.Services.SingleOrDefaultAsync(
                service => service.Id == input.Id && service.BusinessId == business.Id, token);
            if (input.Id is not null && item is null) return NotFound();
            if (item is null)
            {
                item = new Service(business.Id, input.Name, input.Description ?? "", input.Price, input.DurationMinutes, input.BufferMinutes);
                db.Services.Add(item);
            }
            else item.Update(input.Name, input.Description ?? "", input.Price, input.DurationMinutes, input.BufferMinutes, input.IsActive);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            TempData["Notice"] = "Servicio guardado.";
            return RedirectToAction(nameof(Services));
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("Services", new ServiceListPage(business.Name,
                await db.Services.Where(service => service.BusinessId == business.Id).OrderBy(service => service.Name).ToListAsync(token), input));
        }
    }

    [HttpPost("servicios/{id:guid}/estado"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleService(Guid id, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        var item = await db.Services.SingleOrDefaultAsync(service => service.Id == id && service.BusinessId == business.Id, token);
        if (item is null) return NotFound();
        item.Update(item.Name, item.Description, item.Price, item.DurationMinutes, item.BufferMinutes, !item.IsActive);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return RedirectToAction(nameof(Services));
    }

    [HttpPost("servicios/{id:guid}/eliminar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteService(Guid id, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        var item = await db.Services.SingleOrDefaultAsync(service => service.Id == id && service.BusinessId == business.Id, token);
        if (item is null) return NotFound();
        if (await db.Appointments.AnyAsync(appointment => appointment.BusinessId == business.Id && appointment.ServiceId == id, token))
        {
            TempData["Error"] = "No se puede eliminar un servicio que aparece en el historial de citas. Puedes desactivarlo; las citas conservan sus valores originales.";
            return RedirectToAction(nameof(Services));
        }
        db.Services.Remove(item);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return RedirectToAction(nameof(Services));
    }

    [HttpGet("perfil")]
    public async Task<IActionResult> Profile(CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        return View(BuildProfilePage(business));
    }

    private static ProfilePage BuildProfilePage(Business business, ProfileInput? submitted = null) =>
        new(business.Name, business.Slug, business.LogoPath, business.CoverPath, submitted ?? new ProfileInput
        {
            Description = business.Description, Location = business.Location, PublicPhone = business.PublicPhone,
            PublicEmail = business.PublicEmail, TimeZoneId = business.TimeZoneId,
            PrimaryColor = business.PrimaryColor, AccentColor = business.AccentColor,
            ReminderLeadHours = business.ReminderLeadHours
        }, TimeZoneInfo.GetSystemTimeZones()
            .Prepend(TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId))
            .DistinctBy(zone => zone.Id)
            .OrderBy(zone => zone.DisplayName, StringComparer.CurrentCulture).ToList());

    [HttpPost("perfil"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile(ProfileInput input, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        try
        {
            if (string.IsNullOrWhiteSpace(input.TimeZoneId)) throw new TimeZoneNotFoundException();
            _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        }
        catch (TimeZoneNotFoundException) { ModelState.AddModelError(nameof(input.TimeZoneId), "Selecciona una zona horaria válida."); }
        catch (InvalidTimeZoneException) { ModelState.AddModelError(nameof(input.TimeZoneId), "La zona horaria seleccionada no es válida."); }
        if (!ModelState.IsValid) return View("Profile", BuildProfilePage(business, input));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        if (!string.Equals(business.TimeZoneId, input.TimeZoneId, StringComparison.Ordinal)
            && await db.Appointments.AnyAsync(x => x.BusinessId == business.Id && x.Status == AppointmentStatus.Confirmed
                && x.StartUtc.AddMinutes(x.DurationMinutesSnapshot + x.BufferMinutesSnapshot) >= DateTimeOffset.UtcNow, token))
        {
            ModelState.AddModelError(nameof(input.TimeZoneId), "No se puede cambiar la zona horaria mientras haya citas confirmadas futuras. Reprograma o cancela las citas primero.");
            return View("Profile", BuildProfilePage(business, input));
        }
        business.UpdatePublicProfile(input.Description, input.Location, input.PublicPhone, input.PublicEmail,
            input.TimeZoneId, input.PrimaryColor, input.AccentColor);
        business.UpdateReminderLeadHours(input.ReminderLeadHours);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        TempData["Notice"] = "Perfil público actualizado.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("perfil/imagen/{kind}"), ValidateAntiForgeryToken]
    [RequestSizeLimit(3_200_000)]
    public async Task<IActionResult> UploadImage(string kind, IFormFile image, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        if (kind is not ("logo" or "portada") || image is null || image.Length is < 1 or > 3_000_000)
        {
            TempData["Error"] = "Selecciona un logo o portada de hasta 3 MB.";
            return RedirectToAction(nameof(Profile));
        }

        var extension = await DetectImageTypeAsync(image, token);
        if (extension is null)
        {
            TempData["Error"] = "Formato no admitido. Usa JPEG, PNG, WebP o GIF.";
            return RedirectToAction(nameof(Profile));
        }

        // El nombre generado y la carpeta por negocio impiden rutas controladas por el cliente.
        var folder = Path.Combine(environment.WebRootPath, "uploads", business.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(folder, fileName);
        await using (var output = System.IO.File.Create(destination))
            await image.CopyToAsync(output, token);
        var relativePath = $"/uploads/{business.Id:N}/{fileName}";
        if (kind == "logo") business.SetLogoPath(relativePath); else business.SetCoverPath(relativePath);
        await db.SaveChangesAsync(token);
        TempData["Notice"] = "Imagen cargada.";
        return RedirectToAction(nameof(Profile));
    }

    private static async Task<string?> DetectImageTypeAsync(IFormFile image, CancellationToken token)
    {
        var bytes = new byte[12];
        await using var stream = image.OpenReadStream();
        var read = await stream.ReadAsync(bytes, token);
        if (read >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return ".jpg";
        if (read >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (read >= 6 && System.Text.Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a") return ".gif";
        if (read >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return ".webp";
        return null;
    }

    [HttpGet("disponibilidad")]
    public async Task<IActionResult> Schedule(CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        var today = BusinessToday(business);
        return View(new SchedulePage(business.Name,
            await db.WeeklyWindows.Where(item => item.BusinessId == business.Id).OrderBy(item => item.Day).ThenBy(item => item.Start).ToListAsync(token),
            await db.WeeklyBreaks.Where(item => item.BusinessId == business.Id).OrderBy(item => item.Day).ThenBy(item => item.Start).ToListAsync(token),
            await db.ScheduleExceptions.Where(item => item.BusinessId == business.Id).OrderBy(item => item.Date).ToListAsync(token),
            await db.BlockedPeriods.Where(item => item.BusinessId == business.Id).OrderBy(item => item.Date).ThenBy(item => item.Start).ToListAsync(token),
            new WeeklyIntervalInput(), new WeeklyIntervalInput(), new SpecialDateInput { Date = today },
            new BlockedPeriodInput { Date = today }, business.MinimumAdvanceMinutes, business.MaximumBookingDays));
    }

    [HttpPost("disponibilidad/limites"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBookingLimits(int minimumAdvanceMinutes, int maximumBookingDays, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        try
        {
            business.UpdateBookingLimits(minimumAdvanceMinutes, maximumBookingDays);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            TempData["Notice"] = "Límites de reserva guardados. Solo aplican a nuevas citas.";
        }
        catch (ArgumentOutOfRangeException) { TempData["Error"] = "La anticipación debe ser de 0 a 10,080 minutos y el horizonte de 1 a 365 días."; }
        return RedirectToAction(nameof(Schedule));
    }

    private static DateOnly BusinessToday(Business business)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    [HttpPost("disponibilidad/horario"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWindow(WeeklyIntervalInput input, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        if (!Enum.IsDefined(input.Day) || input.End <= input.Start || input.End - input.Start < TimeSpan.FromMinutes(5) || await db.WeeklyWindows.AnyAsync(item => item.BusinessId == business.Id && item.Day == input.Day && item.Start < input.End && input.Start < item.End, token))
            TempData["Error"] = "El horario debe tener un intervalo válido que no se traslape con otro.";
        else
        {
            db.WeeklyWindows.Add(new WeeklyWindow(business.Id, input.Day, input.Start, input.End));
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        return RedirectToAction(nameof(Schedule));
    }

    [HttpPost("disponibilidad/descanso"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBreak(WeeklyIntervalInput input, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        var insideWindow = await db.WeeklyWindows.AnyAsync(item => item.BusinessId == business.Id && item.Day == input.Day && item.Start <= input.Start && item.End >= input.End, token);
        var overlaps = await db.WeeklyBreaks.AnyAsync(item => item.BusinessId == business.Id && item.Day == input.Day && item.Start < input.End && input.Start < item.End, token);
        if (!Enum.IsDefined(input.Day) || input.End <= input.Start || input.End - input.Start < TimeSpan.FromMinutes(5) || !insideWindow || overlaps) TempData["Error"] = "El descanso debe durar al menos 5 minutos, quedar dentro de un horario y no traslaparse con otro.";
        else if (await AppointmentOverlapsLocalIntervalAsync(business, null, input.Day, input.Start, input.End, token)) TempData["Error"] = "No se puede añadir un descanso que se cruce con una cita confirmada. Reprograma o cancela primero.";
        else { db.WeeklyBreaks.Add(new WeeklyBreak(business.Id, input.Day, input.Start, input.End)); await db.SaveChangesAsync(token); await transaction.CommitAsync(token); }
        return RedirectToAction(nameof(Schedule));
    }

    [HttpPost("disponibilidad/fecha-especial"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSpecialDate(SpecialDateInput input, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        var businessToday = BusinessToday(business);
        if (input.Date < businessToday || (!input.IsClosed && (input.Start is null || input.End is null || input.End <= input.Start || input.End - input.Start < TimeSpan.FromMinutes(5))))
            TempData["Error"] = "Elige una fecha futura y, si abre, un intervalo válido.";
        else if (await AppointmentOutsideDateWindowAsync(business, input.Date, input.Start ?? TimeOnly.MinValue, input.End ?? TimeOnly.MaxValue, input.IsClosed, token))
            TempData["Error"] = "Ese cambio de horario afectaría una cita confirmada. Reprograma o cancela la cita antes de guardar.";
        else
        {
            var item = await db.ScheduleExceptions.SingleOrDefaultAsync(row => row.BusinessId == business.Id && row.Date == input.Date, token);
            if (item is null) db.ScheduleExceptions.Add(new ScheduleException(business.Id, input.Date, input.IsClosed, input.Start, input.End));
            else item.Update(input.IsClosed, input.Start, input.End);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        return RedirectToAction(nameof(Schedule));
    }

    [HttpPost("disponibilidad/bloqueo"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBlockedPeriod(BlockedPeriodInput input, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        var overlaps = await db.BlockedPeriods.AnyAsync(row => row.BusinessId == business.Id && row.Date == input.Date && row.Start < input.End && input.Start < row.End, token);
        var businessToday = BusinessToday(business);
        if (input.Date < businessToday || input.End <= input.Start || input.End - input.Start < TimeSpan.FromMinutes(5) || overlaps) TempData["Error"] = "El bloqueo requiere una fecha local vigente y un intervalo de al menos 5 minutos sin traslapes.";
        else if (await AppointmentOverlapsLocalIntervalAsync(business, input.Date, null, input.Start, input.End, token)) TempData["Error"] = "No se puede bloquear un periodo que se cruce con una cita confirmada. Reprograma o cancela primero.";
        else { db.BlockedPeriods.Add(new BlockedPeriod(business.Id, input.Date, input.Start, input.End, input.Reason)); await db.SaveChangesAsync(token); await transaction.CommitAsync(token); }
        return RedirectToAction(nameof(Schedule));
    }

    [HttpPost("disponibilidad/{kind}/{id:guid}/eliminar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScheduleItem(string kind, Guid id, CancellationToken token)
    {
        var business = await OwnBusinessAsync(token);
        if (business is null) return RedirectToAction("Index", "Business");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockBusinessForWriteAsync(business.Id, token);
        switch (kind)
        {
            case "horario":
                var window = await db.WeeklyWindows.SingleOrDefaultAsync(item => item.Id == id && item.BusinessId == business.Id, token);
                if (window is null) return NotFound();
                if (await AppointmentOverlapsLocalIntervalAsync(business, null, window.Day, window.Start, window.End, token))
                { TempData["Error"] = "No se puede retirar un horario semanal que contiene citas confirmadas. Reprograma o cancela primero."; return RedirectToAction(nameof(Schedule)); }
                db.WeeklyWindows.Remove(window);
                var breaks = await db.WeeklyBreaks.Where(item => item.BusinessId == business.Id && item.Day == window.Day && item.Start >= window.Start && item.End <= window.End).ToListAsync(token);
                db.WeeklyBreaks.RemoveRange(breaks);
                break;
            case "descanso":
                var pause = await db.WeeklyBreaks.SingleOrDefaultAsync(item => item.Id == id && item.BusinessId == business.Id, token);
                if (pause is null) return NotFound();
                db.WeeklyBreaks.Remove(pause);
                break;
            case "fecha":
                var special = await db.ScheduleExceptions.SingleOrDefaultAsync(item => item.Id == id && item.BusinessId == business.Id, token);
                if (special is null) return NotFound();
                if (await AppointmentOverlapsLocalIntervalAsync(business, special.Date, null, TimeOnly.MinValue, TimeOnly.MaxValue, token))
                { TempData["Error"] = "No se puede quitar una excepción de fecha con citas confirmadas. Reprograma o cancela primero."; return RedirectToAction(nameof(Schedule)); }
                db.ScheduleExceptions.Remove(special);
                break;
            case "bloqueo":
                var block = await db.BlockedPeriods.SingleOrDefaultAsync(item => item.Id == id && item.BusinessId == business.Id, token);
                if (block is null) return NotFound();
                db.BlockedPeriods.Remove(block);
                break;
            default: return NotFound();
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return RedirectToAction(nameof(Schedule));
    }
}

[AllowAnonymous, Route("b")]
public sealed class PublicBusinessController(ApplicationDbContext db) : Controller
{
    [HttpGet("{slug}/reservar")]
    public async Task<IActionResult> Book(string slug, CancellationToken token)
    {
        var business = await db.Businesses.SingleOrDefaultAsync(item => item.Slug == slug, token);
        if (business is null) return NotFound();
        var services = await db.Services.Where(item => item.BusinessId == business.Id && item.IsActive).OrderBy(item => item.Name).ToListAsync(token);
        return View("Book", new Citalu.Web.Models.PublicBookingPage(business.Name, business.Slug, business.TimeZoneId,
            business.Location, business.PublicPhone, business.PublicEmail, services, Guid.NewGuid()));
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Index(string slug, CancellationToken token)
    {
        var business = await db.Businesses.SingleOrDefaultAsync(item => item.Slug == slug, token);
        if (business is null) return NotFound();
        var services = await db.Services.Where(item => item.BusinessId == business.Id && item.IsActive)
            .OrderBy(item => item.Name).Select(item => new PublicService(item.Name, item.Description, item.Price, item.DurationMinutes)).ToListAsync(token);
        return View(new PublicBusinessPage(business.Name, business.Slug, business.Description, business.Location,
            business.PublicPhone, business.PublicEmail, business.TimeZoneId, business.LogoPath, business.CoverPath,
            business.PrimaryColor, business.AccentColor, services));
    }

    [HttpGet("{slug}/qr.png")]
    public async Task<IActionResult> Qr(string slug, CancellationToken token)
    {
        if (!await db.Businesses.AnyAsync(item => item.Slug == slug, token)) return NotFound();
        var target = Url.Action(nameof(Index), "PublicBusiness", new { slug }, Request.Scheme, Request.Host.Value);
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(target!, QRCoder.QRCodeGenerator.ECCLevel.Q);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
        return File(png, "image/png", $"{slug}-qr.png");
    }
}
