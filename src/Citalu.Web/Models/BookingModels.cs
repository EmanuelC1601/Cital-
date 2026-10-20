using System.ComponentModel.DataAnnotations;
using Citalu.Domain;

namespace Citalu.Web.Models;

public sealed class BookingInput
{
    [Required] public Guid ServiceId { get; set; }
    [Required] public DateOnly Date { get; set; }
    [Required] public TimeOnly Time { get; set; }
    [Required, StringLength(120)] public string CustomerName { get; set; } = "";
    [Required, Phone, StringLength(40)] public string CustomerPhone { get; set; } = "";
    [EmailAddress, StringLength(254)] public string? CustomerEmail { get; set; }
    [Required] public Guid IdempotencyKey { get; set; }
}

public sealed class OwnerAppointmentInput
{
    [Required] public Guid ServiceId { get; set; }
    [Required] public DateOnly Date { get; set; }
    [Required] public TimeOnly Time { get; set; }
    [Required, StringLength(120)] public string CustomerName { get; set; } = "";
    [Required, Phone, StringLength(40)] public string CustomerPhone { get; set; } = "";
    [EmailAddress, StringLength(254)] public string? CustomerEmail { get; set; }
}

public sealed record PublicBookingPage(string BusinessName, string Slug, string TimeZoneId,
    string Location, string Phone, string Email, IReadOnlyList<Service> Services, Guid IdempotencyKey);
public sealed record BookingConfirmation(string BusinessName, string ServiceName, decimal Price, int Duration,
    DateOnly Date, TimeOnly Time, string TimeZoneId, string ManageUrl, Guid Id);
public sealed record OwnerAgendaPage(string BusinessName, string TimeZoneId, DateOnly Date,
    IReadOnlyList<Appointment> Appointments, IReadOnlyList<Service> Services, OwnerAppointmentInput Input);
public sealed record ManageView(string BusinessName, string ServiceName, decimal Price, int Duration,
    DateOnly Date, TimeOnly Time, string TimeZoneId, string Status);

public sealed class ManageCommand
{
    [Required] public string Token { get; set; } = "";
    public string Action { get; set; } = "";
    public DateOnly? Date { get; set; }
    public TimeOnly? Time { get; set; }
}
