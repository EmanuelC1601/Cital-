using Belleza.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Belleza.Infrastructure.Persistence;

/// <summary>Contexto SQL Server de Identity y del dominio de negocio.</summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Belleza.Domain.Service> Services => Set<Belleza.Domain.Service>();
    public DbSet<WeeklyWindow> WeeklyWindows => Set<WeeklyWindow>();
    public DbSet<WeeklyBreak> WeeklyBreaks => Set<WeeklyBreak>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<BlockedPeriod> BlockedPeriods => Set<BlockedPeriod>();

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
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(business => business.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Belleza.Domain.Service>(entity =>
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
    }

    private static void ConfigureSchedule<TEntity>(ModelBuilder builder, string businessKey) where TEntity : class
    {
        var entity = builder.Entity<TEntity>();
        entity.HasKey("Id");
        entity.HasOne<Business>().WithMany().HasForeignKey(businessKey).OnDelete(DeleteBehavior.Cascade);
    }
}
