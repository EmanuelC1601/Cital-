using Citalu.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Citalu.Infrastructure.Persistence;

/// <summary>Contexto SQL Server de Identity y del dominio de negocio.</summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Citalu.Domain.Service> Services => Set<Citalu.Domain.Service>();
    public DbSet<WeeklyWindow> WeeklyWindows => Set<WeeklyWindow>();
    public DbSet<WeeklyBreak> WeeklyBreaks => Set<WeeklyBreak>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<BlockedPeriod> BlockedPeriods => Set<BlockedPeriod>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentChange> AppointmentChanges => Set<AppointmentChange>();
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();
    public DbSet<BusinessSubscription> BusinessSubscriptions => Set<BusinessSubscription>();
    public DbSet<SubscriptionPeriod> SubscriptionPeriods => Set<SubscriptionPeriod>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<SubscriptionAudit> SubscriptionAudits => Set<SubscriptionAudit>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Business>(entity =>
        {
            entity.HasKey(business => business.Id);
            entity.Property(business => business.Name).HasMaxLength(100).IsRequired();
            entity.Property(business => business.Slug).HasMaxLength(64).IsRequired();
            entity.HasIndex(business => business.Slug).IsUnique();
            entity.Property(business => business.OwnerUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(business => business.OwnerUserId).IsUnique();
            entity.Property(business => business.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(business => business.Description).HasMaxLength(1000).IsRequired();
            entity.Property(business => business.Location).HasMaxLength(250).IsRequired();
            entity.Property(business => business.PublicPhone).HasMaxLength(40).IsRequired();
            entity.Property(business => business.PublicEmail).HasMaxLength(254).IsRequired();
            entity.Property(business => business.LogoPath).HasMaxLength(400).IsRequired();
            entity.Property(business => business.CoverPath).HasMaxLength(400).IsRequired();
            entity.Property(business => business.PrimaryColor).HasMaxLength(7).HasDefaultValue("#854f6c").IsRequired();
            entity.Property(business => business.AccentColor).HasMaxLength(7).HasDefaultValue("#f3e9ef").IsRequired();
            entity.Property(business => business.MinimumAdvanceMinutes).HasDefaultValue(60);
            entity.Property(business => business.MaximumBookingDays).HasDefaultValue(60);
            entity.Property(business => business.ReminderLeadHours).HasDefaultValue(24);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(business => business.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Citalu.Domain.Service>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(500).IsRequired();
            entity.Property(item => item.Price).HasPrecision(8, 2);
            entity.HasIndex(item => new { item.BusinessId, item.Name });
            entity.HasOne<Business>().WithMany().HasForeignKey(item => item.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        ConfigureSchedule<WeeklyWindow>(builder, nameof(WeeklyWindow.BusinessId));
        ConfigureSchedule<WeeklyBreak>(builder, nameof(WeeklyBreak.BusinessId));
        ConfigureSchedule<ScheduleException>(builder, nameof(ScheduleException.BusinessId));
        ConfigureSchedule<BlockedPeriod>(builder, nameof(BlockedPeriod.BusinessId));
        builder.Entity<ScheduleException>().HasIndex(item => new { item.BusinessId, item.Date }).IsUnique();
        builder.Entity<BlockedPeriod>().Property(item => item.Reason).HasMaxLength(200).IsRequired();
        builder.Entity<Appointment>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CustomerName).HasMaxLength(120).IsRequired();
            entity.Property(item => item.CustomerPhone).HasMaxLength(40).IsRequired();
            entity.Property(item => item.CustomerEmail).HasMaxLength(254).IsRequired();
            entity.Property(item => item.ServiceNameSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(item => item.PriceSnapshot).HasPrecision(8, 2);
            entity.Property(item => item.ManageTokenHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ManageTokenCiphertext).HasMaxLength(500).IsRequired();
            entity.Property(item => item.IdempotencyKeyHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(item => new { item.BusinessId, item.StartUtc });
            entity.HasIndex(item => new { item.BusinessId, item.ManageTokenHash }).IsUnique();
            entity.HasIndex(item => new { item.BusinessId, item.IdempotencyKeyHash }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(item => item.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Citalu.Domain.Service>().WithMany().HasForeignKey(item => item.ServiceId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<AppointmentChange>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ChangeType).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Actor).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Note).HasMaxLength(500).IsRequired();
            entity.HasIndex(item => new { item.BusinessId, item.AppointmentId, item.CreatedAtUtc });
            entity.HasOne<Appointment>().WithMany().HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<EmailOutboxMessage>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Recipient).HasMaxLength(254).IsRequired();
            entity.Property(item => item.Subject).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ProtectedBody).HasMaxLength(12000).IsRequired();
            entity.Property(item => item.DeduplicationKey).HasMaxLength(150).IsRequired();
            entity.Property(item => item.LastFailure).HasMaxLength(500).IsRequired();
            entity.Property(item => item.LeaseId);
            entity.HasIndex(item => item.DeduplicationKey).IsUnique();
            entity.HasIndex(item => new { item.Status, item.AvailableAtUtc, item.LockedUntilUtc });
            entity.HasIndex(item => new { item.BusinessId, item.AppointmentId, item.Status });
        });
        builder.Entity<BusinessSubscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BusinessId).IsUnique();
            entity.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SubscriptionPeriod>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.GrantedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.RevocationReason).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.SubscriptionId, x.StartsAtUtc, x.EndsAtUtc });
            entity.HasOne<BusinessSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SubscriptionPayment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(120).IsRequired();
            entity.Property(x => x.RecordedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CorrectionReason).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => new { x.SubscriptionId, x.Reference }).IsUnique();
            entity.HasIndex(x => x.ReversesPaymentId).IsUnique().HasFilter("[ReversesPaymentId] IS NOT NULL");
            entity.HasOne<BusinessSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SubscriptionPayment>().WithMany().HasForeignKey(x => x.ReversesPaymentId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SubscriptionAudit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(40).IsRequired();
            entity.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Detail).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.SubscriptionId, x.AtUtc });
            entity.HasOne<BusinessSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSchedule<TEntity>(ModelBuilder builder, string businessKey) where TEntity : class
    {
        var entity = builder.Entity<TEntity>();
        entity.HasKey("Id");
        entity.HasOne<Business>().WithMany().HasForeignKey(businessKey).OnDelete(DeleteBehavior.Cascade);
    }
}
