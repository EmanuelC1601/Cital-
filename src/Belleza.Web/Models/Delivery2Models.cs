using System.ComponentModel.DataAnnotations;
using Belleza.Domain;

namespace Belleza.Web.Models;

public sealed class ServiceInput
{
    public Guid? Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    [Range(typeof(decimal), "0", "999999.99", ParseLimitsInInvariantCulture = true)] public decimal Price { get; set; }
    [Range(5, 480)] public int DurationMinutes { get; set; } = 60;
    [Range(0, 180)] public int BufferMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record ServiceListPage(string BusinessName, IReadOnlyList<Service> Services, ServiceInput Input);

public sealed class ProfileInput
{
    [StringLength(1000)] public string Description { get; set; } = "";
    [StringLength(250)] public string Location { get; set; } = "";
    [Phone, StringLength(40)] public string PublicPhone { get; set; } = "";
    [EmailAddress, StringLength(254)] public string PublicEmail { get; set; } = "";
    [Required] public string TimeZoneId { get; set; } = "America/Mexico_City";
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$")] public string PrimaryColor { get; set; } = "#854f6c";
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentColor { get; set; } = "#f3e9ef";
}

public sealed record ProfilePage(string BusinessName, string Slug, string LogoPath, string CoverPath,
    ProfileInput Input, IReadOnlyList<TimeZoneInfo> TimeZones);

public sealed class WeeklyIntervalInput
{
    public DayOfWeek Day { get; set; }
    [Required] public TimeOnly Start { get; set; }
    [Required] public TimeOnly End { get; set; }
}

public sealed class SpecialDateInput
{
    [Required] public DateOnly Date { get; set; }
    public bool IsClosed { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}

public sealed class BlockedPeriodInput
{
    [Required] public DateOnly Date { get; set; }
    [Required] public TimeOnly Start { get; set; }
    [Required] public TimeOnly End { get; set; }
    [StringLength(200)] public string Reason { get; set; } = "";
}

public sealed record SchedulePage(string BusinessName, IReadOnlyList<WeeklyWindow> Windows,
    IReadOnlyList<WeeklyBreak> Breaks, IReadOnlyList<ScheduleException> Exceptions,
    IReadOnlyList<BlockedPeriod> BlockedPeriods, WeeklyIntervalInput WindowInput,
    WeeklyIntervalInput BreakInput, SpecialDateInput SpecialInput, BlockedPeriodInput BlockedInput,
    int MinimumAdvanceMinutes, int MaximumBookingDays);

public sealed record PublicService(string Name, string Description, decimal Price, int DurationMinutes);
public sealed record PublicBusinessPage(string Name, string Slug, string Description, string Location,
    string PublicPhone, string PublicEmail, string TimeZoneId, string LogoPath, string CoverPath,
    string PrimaryColor, string AccentColor, IReadOnlyList<PublicService> Services);
