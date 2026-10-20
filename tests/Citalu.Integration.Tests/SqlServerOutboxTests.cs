using System.Data.Common;
using Citalu.Application.Notifications;
using Citalu.Domain;
using Citalu.Infrastructure.Notifications;
using Citalu.Infrastructure.Persistence;
using Citalu.Web.Controllers;
using Citalu.Web.Services;
using Citalu.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Citalu.Integration.Tests;

/// <summary>Estas pruebas crean una base efímera SQL Server; requieren CITALU_SQLSERVER_TEST_CONNECTION.</summary>
public sealed class SqlServerOutboxTests
{
    [Fact]
    public async Task SmtpFailureKeepsConfirmedAppointmentRetryableAndLaterCompletes()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (business, appointment) = await fixture.CreateAppointmentAsync();
        var sender = new ControllableEmailSender { FailNext = true };
        var (_, processor, _) = fixture.CreateProcessor(sender);
        await fixture.EnqueueAsync(business.Id, appointment.Id, "reservation:confirmed", appointment.CreatedAtUtc);

        Assert.True(await processor.ProcessOneAsync(default, fixture.DeduplicationPrefix));
        var afterFailure = await fixture.ReadOutboxAsync("reservation:confirmed");
        Assert.Equal(AppointmentStatus.Confirmed, (await fixture.Db.Appointments.SingleAsync(x => x.Id == appointment.Id)).Status);
        Assert.Equal(OutboxStatus.Pending, afterFailure.Status);
        Assert.Equal(1, afterFailure.Attempts);
        Assert.Equal("IOException", afterFailure.LastFailure);
        var retryDelay = (afterFailure.AvailableAtUtc - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.InRange(retryDelay, 20, 40);

        await fixture.MakeDueAsync(afterFailure.Id);
        Assert.True(await processor.ProcessOneAsync(default, fixture.DeduplicationPrefix));
        var recovered = await fixture.ReadOutboxAsync("reservation:confirmed");
        Assert.Equal(OutboxStatus.Sent, recovered.Status);
        Assert.Equal(2, recovered.Attempts);
        Assert.Equal("", recovered.ProtectedBody);
    }

    [Fact]
    public async Task RollbackDoesNotPersistAppointmentOrNotification()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (business, _) = await fixture.CreateAppointmentAsync();
        var service = await fixture.Db.Services.FirstAsync(x => x.BusinessId == business.Id);
        var appointment = new Appointment(business.Id, service.Id, "Cliente de prueba", "5550101000", "customer@example.test",
            DateTimeOffset.UtcNow.AddDays(3), 250m, 30, 10, service.Name, Hash("token"), "cipher", DateTimeOffset.UtcNow.AddDays(90), Hash(Guid.NewGuid().ToString()));
        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Appointments.Add(appointment);
        await fixture.EnqueueAsync(business.Id, appointment.Id, "atomic:rollback", appointment.CreatedAtUtc, save: false);
        await fixture.Db.SaveChangesAsync();
        await transaction.RollbackAsync();

        await using var verify = fixture.NewDb();
        Assert.False(await verify.Appointments.AnyAsync(x => x.Id == appointment.Id));
        Assert.False(await verify.EmailOutbox.AnyAsync(x => x.DeduplicationKey.EndsWith(":atomic:rollback")));
    }

    [Fact]
    public async Task TwoWorkersDoNotClaimSameWorkAndExpiredOwnerCannotOverwriteNewOwner()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (business, appointment) = await fixture.CreateAppointmentAsync();
        var firstSender = new ControllableEmailSender { Block = true };
        var secondSender = new ControllableEmailSender();
        var (_, first, _) = fixture.CreateProcessor(firstSender);
        var (_, second, _) = fixture.CreateProcessor(secondSender);
        await fixture.EnqueueAsync(business.Id, appointment.Id, "lease:fencing", appointment.CreatedAtUtc);

        var firstTask = first.ProcessOneAsync(default, fixture.DeduplicationPrefix);
        await firstSender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(await second.ProcessOneAsync(default, fixture.DeduplicationPrefix));
        var claimed = await fixture.ReadOutboxAsync("lease:fencing");
        var formerLease = claimed.LeaseId;
        await fixture.ExpireLeaseAsync(claimed.Id);
        Assert.True(await second.ProcessOneAsync(default, fixture.DeduplicationPrefix));
        var newOwnerResult = await fixture.ReadOutboxAsync("lease:fencing");
        Assert.Equal(OutboxStatus.Sent, newOwnerResult.Status);
        Assert.NotEqual(formerLease, newOwnerResult.LeaseId);

        firstSender.Release.TrySetResult();
        await firstTask.WaitAsync(TimeSpan.FromSeconds(45));
        var final = await fixture.ReadOutboxAsync("lease:fencing");
        Assert.Equal(OutboxStatus.Sent, final.Status);
        Assert.Equal(1, secondSender.SendCount);
        Assert.Equal(1, firstSender.SendCount);
    }

    [Fact]
    public async Task LeaseRenewalKeepsAWorkItemOwnedDuringLongSend()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (business, appointment) = await fixture.CreateAppointmentAsync();
        var sender = new ControllableEmailSender { Block = true };
        var (_, processor, _) = fixture.CreateProcessor(sender);
        await fixture.EnqueueAsync(business.Id, appointment.Id, "lease:long-send", appointment.CreatedAtUtc);
        var processing = processor.ProcessOneAsync(default, fixture.DeduplicationPrefix);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var initialLease = (await fixture.ReadOutboxAsync("lease:long-send")).LockedUntilUtc;

        // Wait for a durable SQL lease extension, then hold the transport beyond the original two-minute lease.
        var renewed = await fixture.WaitUntilAsync(async () =>
        {
            var row = await fixture.ReadOutboxAsync("lease:long-send");
            return row.LockedUntilUtc.HasValue && initialLease.HasValue && row.LockedUntilUtc.Value > initialLease.Value;
        }, TimeSpan.FromSeconds(50));
        Assert.True(renewed);
        Assert.True(await fixture.WaitUntilAsync(() => Task.FromResult(DateTimeOffset.UtcNow > initialLease), TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30)));
        var duringSend = await fixture.ReadOutboxAsync("lease:long-send");
        Assert.Equal(OutboxStatus.Processing, duringSend.Status);
        Assert.True(duringSend.LockedUntilUtc > DateTimeOffset.UtcNow);

        sender.Release.TrySetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(OutboxStatus.Sent, (await fixture.ReadOutboxAsync("lease:long-send")).Status);
    }

    [Fact]
    public async Task FailedDeliveryStopsAtEightAttempts()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var sender = new ControllableEmailSender { FailNext = true };
        var (_, processor, _) = fixture.CreateProcessor(sender);
        await fixture.EnqueueAsync(null, null, "retry:limit", null);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (attempt > 0) await fixture.MakeDueAsync((await fixture.ReadOutboxAsync("retry:limit")).Id);
            sender.FailNext = true;
            Assert.True(await processor.ProcessOneAsync(default, fixture.DeduplicationPrefix));
        }
        var row = await fixture.ReadOutboxAsync("retry:limit");
        Assert.Equal(OutboxStatus.Failed, row.Status);
        Assert.Equal(8, row.Attempts);
    }

    [Fact]
    public async Task CancellationInvalidatesPendingReminderAndReschedulingQueuesOnlyTheNewVersion()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (business, appointment) = await fixture.CreateAppointmentAsync();
        var oldVersion = appointment.CreatedAtUtc;
        await fixture.EnqueueAsync(business.Id, appointment.Id, "reminder:old", oldVersion);
        await fixture.SupersedeAppointmentMessagesAsync(appointment.Id);
        var cancelledReminder = await fixture.ReadOutboxAsync("reminder:old");
        Assert.Equal(OutboxStatus.Superseded, cancelledReminder.Status);
        Assert.Equal("", cancelledReminder.ProtectedBody);

        await fixture.EnqueueAsync(business.Id, appointment.Id, "reminder:new", oldVersion.AddMinutes(1));
        var rescheduledReminder = await fixture.ReadOutboxAsync("reminder:new");
        Assert.Equal(OutboxStatus.Pending, rescheduledReminder.Status);
        Assert.Equal(oldVersion.AddMinutes(1), rescheduledReminder.AppointmentVersionUtc);
    }

    [Fact]
    public async Task SupersedingAClaimedReminderPreventsTheOldWorkerFromRestoringItsState()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (business, appointment) = await fixture.CreateAppointmentAsync();
        var sender = new ControllableEmailSender { Block = true };
        var (_, processor, _) = fixture.CreateProcessor(sender);
        await fixture.EnqueueAsync(business.Id, appointment.Id, "reminder:claimed", appointment.CreatedAtUtc);
        var processing = processor.ProcessOneAsync(default, fixture.DeduplicationPrefix);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await fixture.SupersedeAppointmentMessagesAsync(appointment.Id);
        sender.Release.TrySetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(20));

        var row = await fixture.ReadOutboxAsync("reminder:claimed");
        Assert.Equal(OutboxStatus.Superseded, row.Status);
        Assert.Equal("", row.ProtectedBody);
    }

    [Fact]
    public async Task WorkItemsRemainSeparatedByBusinessInOwnerQueries()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (firstBusiness, _) = await fixture.CreateAppointmentAsync("owner-one", "business-one");
        var (secondBusiness, _) = await fixture.CreateAppointmentAsync("owner-two", "business-two");
        await fixture.EnqueueAsync(firstBusiness.Id, null, "tenant:first", null);
        await fixture.EnqueueAsync(secondBusiness.Id, null, "tenant:second", null);
        await using var db = fixture.NewDb();
        var firstRows = await db.EmailOutbox.Where(x => x.BusinessId == firstBusiness.Id).Select(x => x.DeduplicationKey).ToListAsync();
        var secondRows = await db.EmailOutbox.Where(x => x.BusinessId == secondBusiness.Id).Select(x => x.DeduplicationKey).ToListAsync();
        Assert.Single(firstRows); Assert.EndsWith(":tenant:first", firstRows[0]);
        Assert.Single(secondRows); Assert.EndsWith(":tenant:second", secondRows[0]);
    }

    [Fact]
    public async Task AgendaAndRetryNotificationCannotReadOrRequeueAnotherBusinessesMessage()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (firstBusiness, firstAppointment) = await fixture.CreateAppointmentAsync("first-owner", "first-business");
        var (secondBusiness, secondAppointment) = await fixture.CreateAppointmentAsync("second-owner", "second-business");
        var firstFailure = fixture.AddFailedOutbox(firstBusiness.Id, firstAppointment.Id, "tenant-failed:first");
        var secondFailure = fixture.AddFailedOutbox(secondBusiness.Id, secondAppointment.Id, "tenant-failed:second");
        await fixture.Db.SaveChangesAsync();

        using var identityScope = fixture.CreateIdentityScope();
        var users = identityScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, firstBusiness.OwnerUserId)], "Integration"))
        };
        var controller = new BookingController(fixture.Db, new AvailabilityCalculator(fixture.Db, TimeProvider.System), users,
            TimeProvider.System, fixture.DataProtection, new OutboxQueue(fixture.Db, fixture.DataProtection),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider())
        };
        var zone = TimeZoneInfo.FindSystemTimeZoneById(firstBusiness.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(firstAppointment.StartUtc, zone).DateTime);
        var agenda = (ViewResult)await controller.Agenda(localDate, default);
        var page = Assert.IsType<OwnerAgendaPage>(agenda.Model);
        Assert.Contains(firstAppointment.Id, page.NotificationStates.Keys);
        Assert.DoesNotContain(secondAppointment.Id, page.NotificationStates.Keys);

        Assert.IsType<NotFoundResult>(await controller.RetryNotification(secondFailure.Id, localDate, default));
        Assert.Equal(OutboxStatus.Failed, (await fixture.Db.EmailOutbox.SingleAsync(x => x.Id == secondFailure.Id)).Status);
        Assert.IsType<RedirectToActionResult>(await controller.RetryNotification(firstFailure.Id, localDate, default));
        var retried = await fixture.Db.EmailOutbox.SingleAsync(x => x.Id == firstFailure.Id);
        Assert.Equal(OutboxStatus.Pending, retried.Status);
        Assert.Equal(0, retried.Attempts);
    }

    [Fact]
    public async Task SqlIdentityConfirmsEmailAndRejectsInvalidOrReusedConfirmationAndResetTokens()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        using var identityScope = fixture.CreateIdentityScope();
        var users = identityScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = $"{Guid.NewGuid():N}@example.test", Email = $"{Guid.NewGuid():N}@example.test" };
        fixture.TrackUser(user.Id);
        Assert.True((await users.CreateAsync(user, "Strong!Pass2026A")).Succeeded);

        var confirmation = await users.GenerateEmailConfirmationTokenAsync(user);
        var db = identityScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queue = new OutboxQueue(db, identityScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var controller = new AccountController(users, null!, queue, TimeProvider.System, db,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        Assert.False(((ViewResult)await controller.Confirm(user.Id, "invalid-confirmation-token")).ViewData["Message"]?.ToString()?.Contains("Correo confirmado") ?? false);
        Assert.Contains("Correo confirmado", ((ViewResult)await controller.Confirm(user.Id, confirmation)).ViewData["Message"]?.ToString());
        Assert.True(await users.IsEmailConfirmedAsync(user));
        Assert.DoesNotContain("Correo confirmado", ((ViewResult)await controller.Confirm(user.Id, confirmation)).ViewData["Message"]?.ToString());

        var reset = await users.GeneratePasswordResetTokenAsync(user);
        Assert.False((await users.ResetPasswordAsync(user, "invalid-reset-token", "New!Password2026B")).Succeeded);
        Assert.True((await users.ResetPasswordAsync(user, reset, "New!Password2026B")).Succeeded);
        Assert.False((await users.ResetPasswordAsync(user, reset, "Other!Password2026C")).Succeeded);
        Assert.True(await users.CheckPasswordAsync(user, "New!Password2026B"));
    }

    [Fact]
    public async Task ManagementLinksRejectInvalidAndExpiredTokensAndRecoveryRotatesOnlyForTheBookingEmail()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (_, appointment) = await fixture.CreateAppointmentAsync();
        var protection = fixture.DataProtection;
        var queue = new OutboxQueue(fixture.Db, protection);
        var controller = new BookingController(fixture.Db, new AvailabilityCalculator(fixture.Db, TimeProvider.System), null!,
            TimeProvider.System, protection, queue, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        var token = fixture.ManagementToken(appointment.Id);
        Assert.IsType<JsonResult>(await controller.ManageData(new ManageCommand { Token = token }, default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.ManageData(new ManageCommand { Token = "invalid-link-token" }, default));

        var wrongEmail = (ViewResult)await controller.RecoverManageLink(new RecoverManageLinkInput
        { AppointmentId = appointment.Id, Email = "someone-else@example.test", RequestId = Guid.NewGuid() }, default);
        Assert.Contains("Si encontramos una cita", wrongEmail.ViewData["Message"]?.ToString());
        Assert.False(await fixture.Db.EmailOutbox.AnyAsync(x => x.AppointmentId == appointment.Id));

        await fixture.Db.Appointments.Where(x => x.Id == appointment.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        fixture.Db.ChangeTracker.Clear();
        Assert.IsType<UnauthorizedObjectResult>(await controller.ManageData(new ManageCommand { Token = token }, default));
        var response = (ViewResult)await controller.RecoverManageLink(new RecoverManageLinkInput
        { AppointmentId = appointment.Id, Email = "customer@example.test", RequestId = Guid.NewGuid() }, default);
        Assert.Contains("Si encontramos una cita", response.ViewData["Message"]?.ToString());
        var mail = await fixture.Db.EmailOutbox.SingleAsync(x => x.AppointmentId == appointment.Id);
        var payload = queue.Unprotect(mail.ProtectedBody);
        Assert.Equal("customer@example.test", payload.Recipient);
        var regenerated = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex
            .Match(payload.Html, "href=\\\"([^\\\"]*citas/gestionar#[^\\\"]+)\\\"").Groups[1].Value).Split('#')[1];

        fixture.Db.ChangeTracker.Clear();
        Assert.IsType<UnauthorizedObjectResult>(await controller.ManageData(new ManageCommand { Token = token }, default));
        Assert.IsType<JsonResult>(await controller.ManageData(new ManageCommand { Token = regenerated }, default));
    }

    private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class ControllableEmailSender : IEmailSender
    {
        private int sendCount;
        public bool FailNext { get; set; }
        public bool Block { get; set; }
        public int SendCount => Volatile.Read(ref sendCount);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref sendCount);
            Entered.TrySetResult();
            if (Block) await Release.Task.WaitAsync(cancellationToken);
            if (FailNext) { FailNext = false; throw new IOException("synthetic transport failure"); }
        }
    }

    private sealed class SqlFixture : IAsyncDisposable
    {
        private readonly string connection;
        private readonly string prefix = $"integration-{Guid.NewGuid():N}:";
        private readonly string keyDirectory;
        private readonly ServiceProvider rootServices;
        private readonly List<ServiceProvider> workerServices = [];
        private readonly List<Guid> businessIds = [];
        private readonly List<Guid> appointmentIds = [];
        private readonly List<string> userIds = [];
        private readonly Dictionary<Guid, string> managementTokens = [];
        public ApplicationDbContext Db { get; }
        public string DeduplicationPrefix => prefix;
        public IDataProtectionProvider DataProtection => rootServices.GetRequiredService<IDataProtectionProvider>();

        private SqlFixture(string connection, string keyDirectory)
        {
            this.connection = connection; this.keyDirectory = keyDirectory;
            rootServices = new ServiceCollection()
                .AddLogging()
                .AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection))
                .AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).Services
                .BuildServiceProvider();
            Db = NewDb();
        }

        public static async Task<SqlFixture> CreateAsync()
        {
            var raw = Environment.GetEnvironmentVariable("CITALU_SQLSERVER_TEST_CONNECTION");
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("Define CITALU_SQLSERVER_TEST_CONNECTION apuntando a SQL Server para ejecutar pruebas de integración.");
            var keys = Path.Combine(Path.GetTempPath(), "CitaluIntegrationKeys", Guid.NewGuid().ToString("N"));
            var fixture = new SqlFixture(raw, keys);
            await fixture.Db.Database.MigrateAsync();
            return fixture;
        }

        public ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);

        public async Task<(Business Business, Appointment Appointment)> CreateAppointmentAsync(string owner = "integration-owner", string slug = "integration-business")
        {
            var user = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = $"{Guid.NewGuid():N}@example.test", Email = $"{Guid.NewGuid():N}@example.test", NormalizedUserName = Guid.NewGuid().ToString().ToUpperInvariant(), NormalizedEmail = Guid.NewGuid().ToString().ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            // Owners are unique per database fixture; labels make the test intent visible.
            user.Id = $"{owner}-{Guid.NewGuid():N}";
            userIds.Add(user.Id);
            var business = new Business(user.Id, "Negocio de integración", $"{slug}-{Guid.NewGuid():N}"[..Math.Min(64, $"{slug}-{Guid.NewGuid():N}".Length)], DateTimeOffset.UtcNow);
            businessIds.Add(business.Id);
            var service = new Citalu.Domain.Service(business.Id, "Servicio", "Prueba de persistencia", 250m, 30, 10);
            Db.Users.Add(user); Db.Businesses.Add(business); Db.Services.Add(service); await Db.SaveChangesAsync();
            var token = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var ciphertext = DataProtection.CreateProtector("Citalu.AppointmentManagement.v1").Protect(token);
            var appointment = new Appointment(business.Id, service.Id, "Cliente prueba", "5550101000", "customer@example.test", DateTimeOffset.UtcNow.AddDays(3), 250m, 30, 10, service.Name, Hash(token), ciphertext, DateTimeOffset.UtcNow.AddDays(90), Hash(Guid.NewGuid().ToString()));
            managementTokens.Add(appointment.Id, token);
            appointmentIds.Add(appointment.Id);
            Db.Appointments.Add(appointment); await Db.SaveChangesAsync();
            return (business, appointment);
        }

        public async Task EnqueueAsync(Guid? businessId, Guid? appointmentId, string key, DateTimeOffset? version, bool save = true)
        {
            var queue = new OutboxQueue(Db, rootServices.GetRequiredService<IDataProtectionProvider>());
            queue.Enqueue(businessId, appointmentId, "recipient@example.test", "Citalú test", "<p>synthetic payload</p>", prefix + key, DateTimeOffset.UtcNow.AddMinutes(-1), version);
            if (save) await queue.SaveAsync(default);
        }

        public async Task<EmailOutboxMessage> ReadOutboxAsync(string key)
        {
            await using var db = NewDb(); return await db.EmailOutbox.AsNoTracking().SingleAsync(x => x.DeduplicationKey == prefix + key);
        }

        public async Task MakeDueAsync(Guid id)
        {
            await using var db = NewDb();
            await db.EmailOutbox.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AvailableAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        public async Task ExpireLeaseAsync(Guid id)
        {
            await using var db = NewDb();
            await db.EmailOutbox.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntilUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        public async Task SupersedeAppointmentMessagesAsync(Guid appointmentId)
        {
            await using var db = NewDb();
            var protection = rootServices.GetRequiredService<IDataProtectionProvider>();
            await new OutboxQueue(db, protection).SupersedeAppointmentMessagesAsync(appointmentId, default);
        }

        public void TrackUser(string userId) => userIds.Add(userId);
        public string ManagementToken(Guid appointmentId) => managementTokens[appointmentId];

        public EmailOutboxMessage AddFailedOutbox(Guid businessId, Guid appointmentId, string key)
        {
            var item = new EmailOutboxMessage(businessId, appointmentId, "customer@example.test", "Citalú test",
                "protected-test-payload", prefix + key, DateTimeOffset.UtcNow, null);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                item.Claim(DateTimeOffset.UtcNow.AddMinutes(2));
                item.Retry(DateTimeOffset.UtcNow, "IOException");
            }
            Db.EmailOutbox.Add(item);
            return item;
        }

        public IServiceScope CreateIdentityScope()
        {
            var provider = new ServiceCollection().AddLogging()
                .AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection))
                .AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).Services
                .AddIdentityCore<IdentityUser>(options => options.Password.RequiredLength = 12)
                .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders().Services
                .BuildServiceProvider();
            workerServices.Add(provider);
            return provider.CreateScope();
        }

        public (ServiceProvider Services, OutboxProcessor Processor, ControllableEmailSender Sender) CreateProcessor(ControllableEmailSender sender)
        {
            var services = new ServiceCollection().AddLogging()
                .AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection))
                .AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).Services
                .AddScoped<OutboxQueue>().AddScoped<IEmailSender>(_ => sender).BuildServiceProvider();
            workerServices.Add(services);
            var processor = new OutboxProcessor(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<OutboxProcessor>.Instance);
            return (services, processor, sender);
        }

        public async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await condition()) return true;
                await Task.Delay(100);
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            foreach (var worker in workerServices) await worker.DisposeAsync();
            await rootServices.DisposeAsync();
            await using var cleanup = NewDb();
            await cleanup.EmailOutbox.Where(x => x.DeduplicationKey.StartsWith(prefix)).ExecuteDeleteAsync();
            await cleanup.Appointments.Where(x => appointmentIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cleanup.Services.Where(x => businessIds.Contains(x.BusinessId)).ExecuteDeleteAsync();
            await cleanup.Businesses.Where(x => businessIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cleanup.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
            if (Directory.Exists(keyDirectory)) Directory.Delete(keyDirectory, true);
        }
    }
}
