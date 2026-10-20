using System.Data;
using System.Security.Cryptography;
using System.Text;
using Citalu.Domain;
using Citalu.Infrastructure.Persistence;
using Citalu.Web.Models;
using Citalu.Web.Services;
using Citalu.Infrastructure.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Citalu.Web.Controllers;

[Route("citas")]
public sealed class BookingController(ApplicationDbContext db, AvailabilityCalculator availability,
    UserManager<IdentityUser> users, TimeProvider clock, IDataProtectionProvider dataProtection, OutboxQueue outbox, IConfiguration configuration) : Controller
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(90);
    private readonly IDataProtector tokenProtector = dataProtection.CreateProtector("Citalu.AppointmentManagement.v1");
    // Lee tokens ya cifrados con el propósito anterior; las nuevas citas usan exclusivamente Citalu.
    private readonly IDataProtector legacyTokenProtector = dataProtection.CreateProtector("Belleza.AppointmentManagement.v1");

    [AllowAnonymous, HttpGet("disponibilidad/{slug}/{serviceId:guid}"), EnableRateLimiting("public-booking")]
    public async Task<IActionResult> Slots(string slug, Guid serviceId, DateOnly date, CancellationToken token)
    {
        var business = await db.Businesses.SingleOrDefaultAsync(x => x.Slug == slug, token);
        if (business is null) return NotFound();
        var service = await db.Services.SingleOrDefaultAsync(x => x.Id == serviceId && x.BusinessId == business.Id && x.IsActive, token);
        if (service is null) return NotFound();
        return Json(new { date, slots = await availability.GetSlotsAsync(business, service, date, null, token) });
    }

    [AllowAnonymous, HttpPost("reservar/{slug}"), ValidateAntiForgeryToken, EnableRateLimiting("public-booking")]
    public async Task<IActionResult> Book(string slug, [FromBody] BookingInput input, CancellationToken token)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Revisa los datos e inténtalo de nuevo.", fields = ModelState.Where(x => x.Value?.Errors.Count > 0).Select(x => x.Key).ToArray() });
        var business = await db.Businesses.SingleOrDefaultAsync(x => x.Slug == slug, token);
        if (business is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        business = await LockBusinessAsync(business.Id, token);
        if (business is null) return NotFound();

        var keyHash = Hash(input.IdempotencyKey.ToString("N"));
        var duplicate = await db.Appointments.SingleOrDefaultAsync(x => x.BusinessId == business.Id && x.IdempotencyKeyHash == keyHash, token);
        if (duplicate is not null && duplicate.Status != AppointmentStatus.Confirmed)
            return Conflict(new { message = "Este intento de reserva ya tiene un estado final. Actualiza la página para iniciar una nueva solicitud." });
        if (duplicate is not null)
        {
            // La copia cifrada permite repetir la respuesta sin guardar el enlace en claro ni emitir otro token.
            var existingToken = UnprotectManageToken(duplicate.ManageTokenCiphertext);
            await tx.CommitAsync(token);
            return Json(Confirmation(business, duplicate, existingToken));
        }
        var service = await db.Services.SingleOrDefaultAsync(x => x.Id == input.ServiceId && x.BusinessId == business.Id && x.IsActive, token);
        if (service is null) return BadRequest(new { message = "Este servicio ya no está disponible." });
        var slots = await availability.GetSlotsAsync(business, service, input.Date, null, token);
        if (!slots.Contains(input.Time)) return Conflict(new { message = "Otra persona ocupó ese horario o ya no está disponible. Elige otro; conservamos tus datos." });

        var start = LocalToUtc(input.Date, input.Time, business);
        var manageToken = CreateToken();
        var appointment = new Appointment(business.Id, service.Id, input.CustomerName, input.CustomerPhone,
            input.CustomerEmail ?? "", start, service.Price, service.DurationMinutes, service.BufferMinutes,
            service.Name, Hash(manageToken), tokenProtector.Protect(manageToken), clock.GetUtcNow().Add(TokenLifetime), keyHash);
        db.Appointments.Add(appointment);
        db.AppointmentChanges.Add(new AppointmentChange(appointment.Id, business.Id, "Reservada", "Cliente", null, start));
        await QueueAppointmentEmails(business, appointment, manageToken, "confirmación", token);
        var reminderAt = start.AddHours(-business.ReminderLeadHours);
        if (reminderAt > clock.GetUtcNow())
            QueueEmail(appointment, appointment.CustomerEmail, "Recordatorio de tu cita",
                $"<p>Te esperamos en {System.Net.WebUtility.HtmlEncode(business.Name)} para {System.Net.WebUtility.HtmlEncode(appointment.ServiceNameSnapshot)}.</p>",
                $"appointment:{appointment.Id}:reminder:{appointment.StartUtc:O}", reminderAt, appointment.CreatedAtUtc);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return Json(Confirmation(business, appointment, manageToken));
    }

    [Authorize, HttpGet("agenda")]
    public async Task<IActionResult> Agenda(DateOnly? date, CancellationToken token)
    {
        var business = await OwnedBusiness(token);
        if (business is null) return RedirectToAction("Index", "Business");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var selectedDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var (from, until) = LocalDay(selectedDate, business);
        var appointments = await db.Appointments.Where(x => x.BusinessId == business.Id && x.StartUtc >= from && x.StartUtc < until)
            .OrderBy(x => x.StartUtc).ToListAsync(token);
        var appointmentIds = appointments.Select(x => x.Id).ToArray();
        var mailStates = await db.EmailOutbox.Where(x => x.BusinessId == business.Id && x.AppointmentId.HasValue && appointmentIds.Contains(x.AppointmentId.Value))
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, x.AppointmentId, x.Status }).ToListAsync(token);
        var statusNames = mailStates.GroupBy(x => x.AppointmentId!.Value).ToDictionary(group => group.Key, group => group.First().Status switch
        { OutboxStatus.Pending or OutboxStatus.Processing => "Notificación en cola", OutboxStatus.Sent => "Notificación enviada", OutboxStatus.Failed => "Notificación pendiente de revisión", _ => "Notificación sustituida" });
        var failedIds = mailStates.Where(x => x.Status == OutboxStatus.Failed).GroupBy(x => x.AppointmentId!.Value).ToDictionary(group => group.Key, group => group.First().Id);
        return View(new OwnerAgendaPage(business.Name, business.TimeZoneId, selectedDate, appointments,
            await db.Services.Where(x => x.BusinessId == business.Id).OrderBy(x => x.Name).ToListAsync(token), new OwnerAppointmentInput { Date = selectedDate }, statusNames, failedIds));
    }

    [Authorize, HttpPost("agenda/crear"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Manual(OwnerAppointmentInput input, CancellationToken token)
    {
        var business = await OwnedBusiness(token);
        if (business is null) return NotFound();
        if (!ModelState.IsValid) { TempData["Error"] = "Revisa los datos de la cita."; return RedirectToAction(nameof(Agenda), new { date = input.Date }); }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        business = await LockBusinessAsync(business.Id, token);
        var service = await db.Services.SingleOrDefaultAsync(x => x.BusinessId == business!.Id && x.Id == input.ServiceId && x.IsActive, token);
        if (service is null) return BadRequest("Selecciona un servicio activo.");
        var slots = await availability.GetSlotsAsync(business!, service, input.Date, null, token);
        if (!slots.Contains(input.Time)) { await tx.RollbackAsync(token); TempData["Error"] = "Ese horario ya no está disponible."; return RedirectToAction(nameof(Agenda), new { date = input.Date }); }
        var start = LocalToUtc(input.Date, input.Time, business!);
        var manageToken = CreateToken();
        var appointment = new Appointment(business!.Id, service.Id, input.CustomerName, input.CustomerPhone,
            input.CustomerEmail ?? "", start, service.Price, service.DurationMinutes, service.BufferMinutes,
            service.Name, Hash(manageToken), tokenProtector.Protect(manageToken), clock.GetUtcNow().Add(TokenLifetime), Hash(Guid.NewGuid().ToString("N")));
        db.Appointments.Add(appointment);
        db.AppointmentChanges.Add(new AppointmentChange(appointment.Id, business.Id, "Reservada", "Propietario", null, start));
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        TempData["ManageLink"] = $"/citas/gestionar#{manageToken}";
        return RedirectToAction(nameof(Agenda), new { date = input.Date });
    }

    [Authorize, HttpPost("agenda/{id:guid}/estado"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(Guid id, string action, DateOnly date, CancellationToken token)
    {
        var business = await OwnedBusiness(token); if (business is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        business = await LockBusinessAsync(business.Id, token);
        var item = await db.Appointments.SingleOrDefaultAsync(x => x.Id == id && x.BusinessId == business!.Id, token);
        if (item is null) return NotFound();
        var old = item.StartUtc;
        if ((action is "atendida" or "inasistencia") && item.OccupiedUntilUtc > clock.GetUtcNow())
            return Conflict("Podrás marcar el resultado cuando termine el horario reservado.");
        try
        {
            if (action == "cancelar") item.Cancel();
            else if (action == "atendida") item.Complete(false);
            else if (action == "inasistencia") item.Complete(true);
            else return BadRequest();
        }
        catch (InvalidOperationException) { return Conflict("La cita ya tiene un estado final."); }
        db.AppointmentChanges.Add(new AppointmentChange(item.Id, business!.Id, item.Status.ToString(), "Propietario", old, null));
        await QueueAppointmentEmails(business, item, UnprotectManageToken(item.ManageTokenCiphertext), "cancelación", token);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return RedirectToAction(nameof(Agenda), new { date });
    }

    [Authorize, HttpPost("agenda/{id:guid}/reprogramar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> OwnerReschedule(Guid id, DateOnly date, TimeOnly time, CancellationToken token)
    {
        var business = await OwnedBusiness(token); if (business is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        business = await LockBusinessAsync(business.Id, token);
        var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == id && x.BusinessId == business!.Id, token);
        if (appointment is null) return NotFound();
        var original = appointment.StartUtc;
        if (appointment.Status != AppointmentStatus.Confirmed) { TempData["Error"] = "Solo puedes reprogramar citas confirmadas."; return RedirectToAction(nameof(Agenda), new { date }); }
        var service = new ServiceSnapshotAdapter(appointment).ToService();
        var available = await availability.GetSlotsAsync(business!, service, date, appointment.Id, token);
        if (!available.Contains(time))
        { await tx.RollbackAsync(token); TempData["Error"] = "Ese horario no está disponible; la cita original se conserva."; return RedirectToAction(nameof(Agenda), new { date }); }
        var start = LocalToUtc(date, time, business!); var newToken = CreateToken();
        appointment.Reschedule(start, Hash(newToken), tokenProtector.Protect(newToken), clock.GetUtcNow().Add(TokenLifetime));
        db.AppointmentChanges.Add(new AppointmentChange(appointment.Id, business!.Id, "Reprogramada", "Propietario", original, start));
        await SupersedePendingEmails(appointment.Id, token);
        await QueueAppointmentEmails(business, appointment, newToken, "reprogramación", token);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        TempData["ManageLink"] = $"/citas/gestionar#{newToken}";
        return RedirectToAction(nameof(Agenda), new { date });
    }

    [Authorize, HttpPost("agenda/notificaciones/{id:guid}/reintentar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryNotification(Guid id, DateOnly date, CancellationToken token)
    {
        var business = await OwnedBusiness(token);
        if (business is null) return NotFound();
        var message = await db.EmailOutbox.SingleOrDefaultAsync(x => x.Id == id && x.BusinessId == business.Id, token);
        if (message is null) return NotFound();
        try { message.RetryNow(clock.GetUtcNow()); await db.SaveChangesAsync(token); TempData["Notice"] = "El correo quedó de nuevo en la cola."; }
        catch (InvalidOperationException) { TempData["Error"] = "El contenido ya venció o ese correo no admite reintento."; }
        return RedirectToAction(nameof(Agenda), new { date });
    }

    [AllowAnonymous, HttpGet("gestionar")]
    public IActionResult Manage() => View();

    [AllowAnonymous, HttpGet("recuperar-enlace")]
    public IActionResult RecoverManageLink() => View(new RecoverManageLinkInput { RequestId = Guid.NewGuid() });

    [AllowAnonymous, HttpPost("recuperar-enlace"), ValidateAntiForgeryToken, EnableRateLimiting("public-booking")]
    public async Task<IActionResult> RecoverManageLink(RecoverManageLinkInput input, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            var candidate = await db.Appointments.SingleOrDefaultAsync(x => x.Id == input.AppointmentId, token);
            if (candidate is not null && candidate.Status == AppointmentStatus.Confirmed &&
                !string.IsNullOrWhiteSpace(candidate.CustomerEmail) &&
                string.Equals(candidate.CustomerEmail, input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
                var business = await LockBusinessAsync(candidate.BusinessId, token);
                var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == candidate.Id && x.BusinessId == business!.Id, token);
                if (appointment is not null) await db.Entry(appointment).ReloadAsync(token);
                if (appointment is not null && appointment.Status == AppointmentStatus.Confirmed &&
                    string.Equals(appointment.CustomerEmail, input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var current = clock.GetUtcNow();
                    var manageToken = appointment.TokenExpiresAtUtc <= current ? CreateToken() : UnprotectManageToken(appointment.ManageTokenCiphertext);
                    if (appointment.TokenExpiresAtUtc <= current)
                        appointment.RotateManageToken(Hash(manageToken), tokenProtector.Protect(manageToken), current.Add(TokenLifetime));
                    var baseUrl = configuration["Application:PublicBaseUrl"]?.TrimEnd('/') ?? "http://localhost:5000";
                    var link = $"{baseUrl}/citas/gestionar#{manageToken}";
                    outbox.Enqueue(business!.Id, appointment.Id, appointment.CustomerEmail, "Recupera el enlace de tu cita en Citalú",
                        $"<p>Usa este enlace privado para consultar o gestionar tu cita: <a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">abrir gestión</a>.</p>",
                        $"appointment:{appointment.Id}:manage-recovery:{input.RequestId:N}", current, appointment.UpdatedAtUtc ?? appointment.CreatedAtUtc);
                    await db.SaveChangesAsync(token);
                    await tx.CommitAsync(token);
                }
            }
        }
        ViewData["Message"] = "Si encontramos una cita con esos datos, enviaremos un enlace al correo asociado.";
        return View("RecoveryMessage");
    }

    [AllowAnonymous, HttpPost("gestionar/datos"), IgnoreAntiforgeryToken, EnableRateLimiting("public-booking")]
    public async Task<IActionResult> ManageData([FromBody] ManageCommand command, CancellationToken token)
    {
        var result = await FindByToken(command.Token, token);
        if (result is null) return Unauthorized(new { message = "El enlace es inválido o venció." });
        var (business, appointment) = result.Value;
        var local = TimeZoneInfo.ConvertTime(appointment.StartUtc, TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId));
        return Json(new ManageView(business.Name, appointment.ServiceNameSnapshot, appointment.PriceSnapshot,
            appointment.DurationMinutesSnapshot, DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime),
            business.TimeZoneId, appointment.Status.ToString()));
    }

    [AllowAnonymous, HttpPost("gestionar/horarios"), IgnoreAntiforgeryToken, EnableRateLimiting("public-booking")]
    public async Task<IActionResult> ManageSlots([FromBody] ManageCommand command, CancellationToken token)
    {
        var found = await FindByToken(command.Token, token);
        if (found is null) return Unauthorized(new { message = "El enlace es inválido o venció." });
        var (business, appointment) = found.Value;
        var service = new ServiceSnapshotAdapter(appointment).ToService();
        return Json(new { slots = await availability.GetSlotsAsync(business, service, command.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), appointment.Id, token) });
    }

    [AllowAnonymous, HttpPost("gestionar/accion"), IgnoreAntiforgeryToken, EnableRateLimiting("public-booking")]
    public async Task<IActionResult> ManageAction([FromBody] ManageCommand command, CancellationToken token)
    {
        var found = await FindByToken(command.Token, token);
        if (found is null) return Unauthorized(new { message = "El enlace es inválido o venció." });
        var (business, initial) = found.Value;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        business = await LockBusinessAsync(business.Id, token);
        var appointment = await db.Appointments.SingleAsync(x => x.Id == initial.Id && x.BusinessId == business!.Id, token);
        if (appointment.ManageTokenHash != Hash(command.Token) || appointment.TokenExpiresAtUtc <= clock.GetUtcNow()) return Unauthorized(new { message = "El enlace ya no es válido." });
        if (appointment.Status != AppointmentStatus.Confirmed) return Conflict(new { message = "La cita ya tiene un estado final." });
        if (command.Action == "cancelar")
        {
            var old = appointment.StartUtc; appointment.Cancel();
            db.AppointmentChanges.Add(new AppointmentChange(appointment.Id, business!.Id, "Cancelada", "Cliente", old, null));
            await SupersedePendingEmails(appointment.Id, token);
            await QueueAppointmentEmails(business, appointment, UnprotectManageToken(appointment.ManageTokenCiphertext), "cancelación", token);
        }
        else if (command.Action == "reprogramar" && command.Date.HasValue && command.Time.HasValue)
        {
            // Las reglas de la cita conservan el servicio aceptado al reservar, aunque el catálogo cambie.
            var service = new ServiceSnapshotAdapter(appointment).ToService();
            var slots = await availability.GetSlotsAsync(business!, service, command.Date.Value, appointment.Id, token);
            if (!slots.Contains(command.Time.Value)) return Conflict(new { message = "Ese horario ya no está disponible. La cita original se conserva." });
            var next = LocalToUtc(command.Date.Value, command.Time.Value, business!); var old = appointment.StartUtc;
            var newToken = CreateToken(); appointment.Reschedule(next, Hash(newToken), tokenProtector.Protect(newToken), clock.GetUtcNow().Add(TokenLifetime));
            db.AppointmentChanges.Add(new AppointmentChange(appointment.Id, business!.Id, "Reprogramada", "Cliente", old, next));
            await SupersedePendingEmails(appointment.Id, token);
            await QueueAppointmentEmails(business, appointment, newToken, "reprogramación", token);
            await db.SaveChangesAsync(token); await tx.CommitAsync(token);
            return Json(new { ok = true, manageUrl = $"/citas/gestionar#{newToken}" });
        }
        else return BadRequest(new { message = "Acción no válida." });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return Json(new { ok = true });
    }

    private async Task<Business?> OwnedBusiness(CancellationToken token)
    {
        var owner = users.GetUserId(User);
        return await db.Businesses.SingleOrDefaultAsync(x => x.OwnerUserId == owner, token);
    }
    // Todas las escrituras de agenda toman primero este bloqueo; evita carreras incluso si aún no existe una cita.
    private Task<Business?> LockBusinessAsync(Guid id, CancellationToken token) =>
        db.Businesses.FromSqlInterpolated($"SELECT * FROM dbo.Businesses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            .AsNoTracking().SingleOrDefaultAsync(token);
    private static string CreateToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string UnprotectManageToken(string protectedToken)
    {
        try { return tokenProtector.Unprotect(protectedToken); }
        catch (CryptographicException) { return legacyTokenProtector.Unprotect(protectedToken); }
    }
    private DateTimeOffset LocalToUtc(DateOnly date, TimeOnly time, Business business)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw new ArgumentException("La hora no existe o es ambigua por el cambio horario.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
    private (DateTimeOffset From, DateTimeOffset Until) LocalDay(DateOnly date, Business business)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var from = LocalToUtc(date, TimeOnly.MinValue, business);
        var until = LocalToUtc(date.AddDays(1), TimeOnly.MinValue, business);
        return (from, until);
    }
    private object Confirmation(Business business, Appointment appointment, string manageToken)
    {
        var local = TimeZoneInfo.ConvertTime(appointment.StartUtc, TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId));
        return new { id = appointment.Id, businessName = business.Name, service = appointment.ServiceNameSnapshot,
            price = appointment.PriceSnapshot, duration = appointment.DurationMinutesSnapshot,
            date = DateOnly.FromDateTime(local.DateTime), time = TimeOnly.FromDateTime(local.DateTime),
            timeZone = business.TimeZoneId, manageUrl = $"/citas/gestionar#{manageToken}" };
    }
    private async Task<(Business Business, Appointment Appointment)?> FindByToken(string raw, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 100) return null;
        var hash = Hash(raw);
        var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.ManageTokenHash == hash, token);
        if (appointment is null || appointment.TokenExpiresAtUtc <= clock.GetUtcNow()) return null;
        var business = await db.Businesses.SingleOrDefaultAsync(x => x.Id == appointment.BusinessId, token);
        return business is null ? null : (business, appointment);
    }
    private sealed class ServiceSnapshotAdapter(Appointment appointment)
    {
        public Service ToService() => new(appointment.BusinessId, appointment.ServiceNameSnapshot, "", appointment.PriceSnapshot,
            appointment.DurationMinutesSnapshot, appointment.BufferMinutesSnapshot);
    }

    private async Task QueueAppointmentEmails(Business business, Appointment appointment, string manageToken, string eventName, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var version = appointment.UpdatedAtUtc ?? appointment.CreatedAtUtc;
        var local = TimeZoneInfo.ConvertTime(appointment.StartUtc, TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId));
        var details = $"{System.Net.WebUtility.HtmlEncode(appointment.ServiceNameSnapshot)} · ${appointment.PriceSnapshot:N2} · {local:dd/MM/yyyy HH:mm} ({System.Net.WebUtility.HtmlEncode(business.TimeZoneId)})";
        var baseUrl = configuration["Application:PublicBaseUrl"]?.TrimEnd('/') ?? "http://localhost:5000";
        var manageUrl = $"{baseUrl}/citas/gestionar#{manageToken}";
        QueueEmail(appointment, appointment.CustomerEmail, $"{eventName} de cita en Citalú",
            $"<p>Hola {System.Net.WebUtility.HtmlEncode(appointment.CustomerName)},</p><p>{details}</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(manageUrl)}\">Consultar o gestionar cita</a></p>",
            $"appointment:{appointment.Id}:customer:{eventName}:{version:O}", now, version);
        var owner = await users.FindByIdAsync(business.OwnerUserId);
        if (owner?.EmailConfirmed == true && !string.IsNullOrWhiteSpace(owner.Email))
            QueueEmail(appointment, owner.Email, $"{eventName} · Citalú",
                $"<p>Hay una {eventName}.</p><p>{details}</p><p><a href=\"{baseUrl}/citas/agenda\">Abrir agenda (requiere iniciar sesión)</a></p>",
                $"appointment:{appointment.Id}:owner:{eventName}:{version:O}", now, version);
    }

    private void QueueEmail(Appointment appointment, string recipient, string subject, string html, string key, DateTimeOffset at, DateTimeOffset? version) =>
        outbox.Enqueue(appointment.BusinessId, appointment.Id, recipient, subject, html, key, at, version);

    private Task SupersedePendingEmails(Guid appointmentId, CancellationToken token) =>
        outbox.SupersedeAppointmentMessagesAsync(appointmentId, token);
}
