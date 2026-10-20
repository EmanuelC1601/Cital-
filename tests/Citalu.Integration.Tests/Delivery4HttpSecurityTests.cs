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

    private static WebApplicationFactory<Program> CreateHost(string connection, SensitiveLogProvider? logs = null)
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
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
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
                => entries.Enqueue(formatter(state, exception));
        }
    }
}
