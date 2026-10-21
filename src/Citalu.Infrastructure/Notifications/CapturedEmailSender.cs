using System.Text.Json;
using Citalu.Application.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Citalu.Infrastructure.Notifications;

/// <summary>Capturador local: nunca conecta con servidores externos ni imprime el contenido.</summary>
public sealed class CapturedEmailSender(IWebHostEnvironment environment, IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var demoCaptureOnly = environment.IsEnvironment("Demo") && configuration.GetValue<bool>("Email:CaptureOnly");
        if (!environment.IsDevelopment() && !demoCaptureOnly)
            throw new InvalidOperationException("El capturador solo está permitido en desarrollo o con captura explícita en el entorno Demo.");
        var configured = configuration["Email:CapturePath"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "data", "mail-capture")
            : Path.GetFullPath(configured);
        Directory.CreateDirectory(path);
        // Las capturas locales contienen datos de contacto y enlaces privados: retención máxima de 7 días.
        foreach (var oldFile in Directory.EnumerateFiles(path, "*.json").Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)))
            File.Delete(oldFile);
        var safeName = Guid.NewGuid().ToString("N") + ".json";
        var temp = Path.Combine(path, safeName + ".tmp");
        var target = Path.Combine(path, safeName);
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(message), cancellationToken);
        File.Move(temp, target);
    }
}
