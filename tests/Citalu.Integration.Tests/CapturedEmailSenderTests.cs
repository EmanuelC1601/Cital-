using Citalu.Application.Notifications;
using Citalu.Infrastructure.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Citalu.Integration.Tests;

public sealed class CapturedEmailSenderTests
{
    [Fact]
    public async Task DemoCaptureOnlyWritesToConfiguredPrivateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "Citalu-mail-capture-tests", Guid.NewGuid().ToString("N"));
        var capture = Path.Combine(root, "private-capture");
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:CaptureOnly"] = "true",
                ["Email:CapturePath"] = capture
            }).Build();
            var sender = new CapturedEmailSender(new TestWebHostEnvironment(root, "Demo"), config);

            await sender.SendAsync(new EmailMessage("demo@example.invalid", "Demo", "private test body", "test-idempotency"), default);

            var file = Assert.Single(Directory.GetFiles(capture, "*.json"));
            Assert.Contains("demo@example.invalid", await File.ReadAllTextAsync(file));
            Assert.False(Directory.Exists(Path.Combine(root, "wwwroot", "private-capture")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DemoWithoutCaptureOnlyCannotUseLocalCaptureTransport()
    {
        var root = Path.Combine(Path.GetTempPath(), "Citalu-mail-capture-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var sender = new CapturedEmailSender(new TestWebHostEnvironment(root, "Demo"), config);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sender.SendAsync(new EmailMessage("demo@example.invalid", "Demo", "unused", "test-idempotency"), default));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string root, string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Citalu.Integration.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
