using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Citalu.Infrastructure.Notifications;
using Citalu.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using Xunit;
using SqlFixture = Citalu.Integration.Tests.SqlServerOutboxTests.SqlFixture;

namespace Citalu.Integration.Tests;

public sealed class Delivery4HttpSecurityTests
{
    [Fact]
    public async Task RecoveryHttpLimitRejectsTheSixthRequestPerProcess()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        using var host = CreateHost(fixture.ConnectionString);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var form = await ReadAntiForgeryForm(client);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++)
        {
            last?.Dispose();
            last = await client.PostAsync("/cuenta/recuperar", new FormUrlEncodedContent(form.Append(new KeyValuePair<string, string>("Email", "not-registered@example.test"))));
        }
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, last!.StatusCode);
        last.Dispose();
    }

    [Fact]
    public async Task PasswordRecoveryHttpResponseIsGenericAndLogsNeverContainAddressOrResetToken()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var logs = new SensitiveLogProvider();
        using var host = CreateHost(fixture.ConnectionString, logs);
        var email = $"private-{Guid.NewGuid():N}@example.test";
        const string password = "Synthetic-Private-Password-529!";
        string userId;
        using (var scope = host.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, password);
            Assert.True(result.Succeeded);
            userId = user.Id;
            fixture.TrackUser(userId);
        }
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var form = await ReadAntiForgeryForm(client);
        logs.Clear();
        var response = await client.PostAsync("/cuenta/recuperar", new FormUrlEncodedContent(form.Append(new KeyValuePair<string, string>("Email", email))));
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        var responsePage = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("Si la cuenta existe y puede recuperarse", responsePage);

        using var db = fixture.NewDb();
        var row = await db.EmailOutbox.SingleAsync(x => x.DeduplicationKey.StartsWith($"account:password-reset:{userId}:"));
        var protector = host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Citalu.EmailOutbox.Payload.v1");
        var payload = OutboxPayloadReader.Read(protector.Unprotect(row.ProtectedBody));
        var token = Regex.Match(payload, "code=([^&\\\"]+)").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.DoesNotContain(email, logs.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredOwnerCannotCreateAppointmentsButCanViewExportAndCancelExistingOnes()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var (business, appointment) = await fixture.CreateAppointmentAsync();
        var serviceId = await fixture.Db.Appointments.Where(x => x.Id == appointment.Id).Select(x => x.ServiceId).SingleAsync();

        var logs = new SensitiveLogProvider();
        using var host = CreateHost(fixture.ConnectionString, logs, ownerTestAuthentication: true, protectionProvider: fixture.DataProtection);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Owner", business.OwnerUserId);

        var publicBookingPage = await client.GetStringAsync($"/b/{business.Slug}/reservar");
        var publicAntiforgery = System.Net.WebUtility.HtmlDecode(Regex.Match(publicBookingPage,
            "name=\\\"__RequestVerificationToken\\\" type=\\\"hidden\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value);
        Assert.False(string.IsNullOrWhiteSpace(publicAntiforgery));
        var now = DateTimeOffset.UtcNow;
        await fixture.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.BusinessSubscriptions SET TrialStartsAtUtc = {now.AddDays(-31)}, TrialEndsAtUtc = {now.AddDays(-1)}, Status = 2 WHERE BusinessId = {business.Id}");

        var adminPage = await client.GetAsync("/plataforma/suscripciones");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, adminPage.StatusCode);

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartUtc, TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId)).DateTime);
        var expiredBookPage = await client.GetAsync($"/b/{business.Slug}/reservar");
        Assert.Contains("reservas están temporalmente deshabilitadas", await expiredBookPage.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var slotsResponse = await client.GetAsync($"/citas/disponibilidad/{business.Slug}/{serviceId}?date={localDate:yyyy-MM-dd}");
        Assert.Equal(System.Net.HttpStatusCode.OK, slotsResponse.StatusCode);
        Assert.Contains("\"slots\":[]", await slotsResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var publicRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"/citas/reservar/{business.Slug}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                serviceId, date = localDate, time = "10:00", customerName = "Cliente bloqueado",
                customerPhone = "5550101000", customerEmail = (string?)null, idempotencyKey = Guid.NewGuid()
            })
        };
        publicRequest.Headers.TryAddWithoutValidation("RequestVerificationToken", publicAntiforgery);
        var publicReservation = await client.SendAsync(publicRequest);
        Assert.True(publicReservation.StatusCode == System.Net.HttpStatusCode.Locked,
            $"Expected a locked reservation, received {publicReservation.StatusCode}: {await publicReservation.Content.ReadAsStringAsync()}");

        var agenda = await client.GetAsync($"/citas/agenda?date={localDate:yyyy-MM-dd}");
        Assert.Equal(System.Net.HttpStatusCode.OK, agenda.StatusCode);
        var agendaHtml = await agenda.Content.ReadAsStringAsync();
        Assert.Contains("Cliente prueba", agendaHtml, StringComparison.Ordinal);
        var antiforgery = System.Net.WebUtility.HtmlDecode(Regex.Match(agendaHtml,
            "name=\\\"__RequestVerificationToken\\\" type=\\\"hidden\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value);
        Assert.False(string.IsNullOrWhiteSpace(antiforgery));

        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var end = start.AddDays(10);
        var export = await client.GetAsync($"/citas/agenda/exportar?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}");
        Assert.Equal(System.Net.HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("Cliente prueba", await export.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var manual = await client.PostAsync("/citas/agenda/crear", new FormUrlEncodedContent([
            new("__RequestVerificationToken", antiforgery), new("ServiceId", serviceId.ToString()),
            new("Date", localDate.ToString("yyyy-MM-dd")), new("Time", "10:00"),
            new("CustomerName", "No debe crearse"), new("CustomerPhone", "5550101000")
        ]));
        Assert.Equal(System.Net.HttpStatusCode.Found, manual.StatusCode);
        Assert.Equal(1, await fixture.Db.Appointments.CountAsync(x => x.BusinessId == business.Id));

        var cancel = await client.PostAsync($"/citas/agenda/{appointment.Id}/estado", new FormUrlEncodedContent([
            new("__RequestVerificationToken", antiforgery), new("action", "cancelar"), new("date", localDate.ToString("yyyy-MM-dd"))
        ]));
        Assert.True(cancel.StatusCode == System.Net.HttpStatusCode.Found, $"Unexpected cancellation status {cancel.StatusCode}: {logs.Text}");
        Assert.Equal(Citalu.Domain.AppointmentStatus.Cancelled,
            await fixture.Db.Appointments.Where(x => x.Id == appointment.Id).Select(x => x.Status).SingleAsync());
    }

    private static WebApplicationFactory<Program> CreateHost(string connection, SensitiveLogProvider? logs = null,
        bool ownerTestAuthentication = false, IDataProtectionProvider? protectionProvider = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "CitaluHttpTests", Guid.NewGuid().ToString("N"));
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["DataProtection:KeysPath"] = Path.Combine(temp, "keys"),
                ["Email:CapturePath"] = Path.Combine(temp, "capture"),
                ["Application:PublicBaseUrl"] = "http://localhost"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                if (protectionProvider is not null)
                {
                    services.RemoveAll<IDataProtectionProvider>();
                    services.AddSingleton(protectionProvider);
                }
                if (ownerTestAuthentication)
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "TestOwner";
                        options.DefaultChallengeScheme = "TestOwner";
                        options.DefaultForbidScheme = "TestOwner";
                    }).AddScheme<AuthenticationSchemeOptions, OwnerTestAuthenticationHandler>("TestOwner", _ => { });
                }
            });
            if (logs is not null) builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        });
    }

    private static async Task<List<KeyValuePair<string, string>>> ReadAntiForgeryForm(HttpClient client)
    {
        var html = await client.GetStringAsync("/cuenta/recuperar");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));
        return [new("__RequestVerificationToken", System.Net.WebUtility.HtmlDecode(token))];
    }

    private static class OutboxPayloadReader
    {
        public static string Read(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("Html").GetString()!;
    }

    private sealed class OwnerTestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var ownerId = Request.Headers["X-Test-Owner"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(ownerId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, ownerId), new Claim(ClaimTypes.Name, ownerId)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class SensitiveLogProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> entries = new();
        public string Text => string.Join("\n", entries);
        public void Clear() { while (entries.TryDequeue(out _)) { } }
        public ILogger CreateLogger(string categoryName) => new SensitiveLogger(entries);
        public void Dispose() { }
        private sealed class SensitiveLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue(exception is null ? formatter(state, exception) : $"{formatter(state, exception)} {exception}");
        }
    }
}
