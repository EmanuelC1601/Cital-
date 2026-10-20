using Belleza.Domain;

namespace Belleza.Application.Tests;

public sealed class ServiceAndScheduleTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1000000)]
    public void Service_RejectsPriceOutsideSupportedRange(decimal price) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Service(Guid.NewGuid(), "Corte", "", price, 30, 0));

    [Fact]
    public void Service_RejectsDurationOutsideSupportedRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Service(Guid.NewGuid(), "Corte", "", 250m, 0, 0));

    [Fact]
    public void WeeklyWindow_RejectsIntervalsShorterThanFiveMinutes() =>
        Assert.Throws<ArgumentException>(() =>
            new WeeklyWindow(Guid.NewGuid(), DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(9, 4)));

    [Fact]
    public void BlockedPeriod_RejectsEndBeforeStart() =>
        Assert.Throws<ArgumentException>(() =>
            new BlockedPeriod(Guid.NewGuid(), new DateOnly(2026, 11, 1), new TimeOnly(14, 0), new TimeOnly(13, 0), "Prueba"));
}
