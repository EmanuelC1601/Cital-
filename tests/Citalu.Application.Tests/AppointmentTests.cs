using Citalu.Domain;

namespace Citalu.Application.Tests;

public sealed class AppointmentTests
{
    private static Appointment CreateAppointment() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Cliente", "5555000000", "", DateTimeOffset.UtcNow.AddDays(1),
        450.50m, 60, 15, "Servicio original", "hash-inicial", "encrypted-token", DateTimeOffset.UtcNow.AddDays(90), "idempotency-hash");

    [Fact]
    public void AppointmentKeepsAgreedPriceDurationAndBufferAsSnapshot()
    {
        var appointment = CreateAppointment();

        Assert.Equal(450.50m, appointment.PriceSnapshot);
        Assert.Equal(60, appointment.DurationMinutesSnapshot);
        Assert.Equal(15, appointment.BufferMinutesSnapshot);
        Assert.Equal("Servicio original", appointment.ServiceNameSnapshot);
    }

    [Fact]
    public void FinalStateCannotTransitionAgain()
    {
        var appointment = CreateAppointment();
        appointment.Complete(noShow: false);

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Throws<InvalidOperationException>(() => appointment.Cancel());
        Assert.Throws<InvalidOperationException>(() => appointment.Reschedule(
            DateTimeOffset.UtcNow.AddDays(2), "new-hash", "encrypted-new-token", DateTimeOffset.UtcNow.AddDays(92)));
    }

    [Fact]
    public void ReschedulingRotatesTheManagementTokenHash()
    {
        var appointment = CreateAppointment();
        var newStart = DateTimeOffset.UtcNow.AddDays(2);

        appointment.Reschedule(newStart, "new-token-hash", "encrypted-token-2", DateTimeOffset.UtcNow.AddDays(92));

        Assert.Equal(newStart, appointment.StartUtc);
        Assert.Equal("new-token-hash", appointment.ManageTokenHash);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
    }
}
