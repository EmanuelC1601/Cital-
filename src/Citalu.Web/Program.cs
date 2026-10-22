using Citalu.Application.Businesses;
using Citalu.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Threading.RateLimiting;
using Citalu.Web.Services;
using Citalu.Web.Controllers;
using Citalu.Infrastructure.Notifications;
using Citalu.Application.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using System.Net;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

// Falla con nombres de opciones faltantes antes de abrir certificados o archivos configurados.
ValidateOperationalConfiguration(builder.Configuration, builder.Environment);

builder.Services.AddControllersWithViews();
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment()) builder.Logging.AddConsole();
else builder.Logging.AddJsonConsole();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-booking", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("account-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0, AutoReplenishment = true }));
});
var uiCulture = CultureInfo.GetCultureInfo("es-MX");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(uiCulture);
    options.SupportedCultures = [uiCulture];
    options.SupportedUICultures = [uiCulture];
});
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.Configure<SubscriptionSettings>(builder.Configuration.GetSection("Subscriptions"));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Citalu.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/cuenta/iniciar-sesion";
    options.AccessDeniedPath = "/cuenta/iniciar-sesion";
});
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddScoped<IBusinessStore, BusinessStore>();
builder.Services.AddScoped<CreateBusinessHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AvailabilityCalculator>();
builder.Services.AddScoped<SubscriptionAccessService>();
builder.Services.AddScoped<ManualPaymentRegistrationService>();
builder.Services.AddScoped<OutboxQueue>();
var demoCaptureOnly = builder.Environment.IsEnvironment("Demo") && builder.Configuration.GetValue<bool>("Email:CaptureOnly");
builder.Services.AddScoped<IEmailSender>(services => builder.Environment.IsDevelopment() || demoCaptureOnly
    ? services.GetRequiredService<CapturedEmailSender>()
    : services.GetRequiredService<SmtpEmailSender>());
builder.Services.AddScoped<CapturedEmailSender>();
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddHostedService<OutboxProcessor>();
builder.Services.AddHostedService<SubscriptionExpiryNotifier>();
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
if (builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(dataProtectionPath))
{
    // Conserva el anillo y el ApplicationDiscriminator actuales para no invalidar cookies ni tokens.
    var keyPath = string.IsNullOrWhiteSpace(dataProtectionPath)
        ? Path.Combine(builder.Environment.ContentRootPath, "data", "keys")
        : dataProtectionPath;
    Directory.CreateDirectory(keyPath);
    var protection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    if (!builder.Environment.IsDevelopment())
    {
        var certificatePath = builder.Configuration["DataProtection:CertificatePath"]!;
        var certificatePassword = builder.Configuration["DataProtection:CertificatePassword"]!;
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword);
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("The Data Protection certificate must include its private key.");
        protection.ProtectKeysWithCertificate(certificate);
    }
}

var forwardedHeadersEnabled = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (forwardedHeadersEnabled)
{
    var proxyAddresses = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in proxyAddresses)
            if (IPAddress.TryParse(address, out var parsed)) options.KnownProxies.Add(parsed);
    });
}

var app = builder.Build();

if (args.Contains("--create-platform-admin", StringComparer.Ordinal))
{
    await BootstrapFirstPlatformAdminAsync(app.Services);
    return;
}

// Configure the HTTP request pipeline.
if (forwardedHeadersEnabled) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// El middleware atiende también imágenes que se cargan después de compilar la aplicación.
app.UseStaticFiles();
var uploadsPath = builder.Configuration["Storage:UploadsPath"];
if (string.IsNullOrWhiteSpace(uploadsPath))
    uploadsPath = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(uploadsPath)),
    RequestPath = "/uploads"
});
app.MapStaticAssets();

// Readiness solo comunica si la aplicación y SQL Server responden; no devuelve configuración ni datos.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/health/ready", async (ApplicationDbContext db, CancellationToken token) =>
{
    try { return await db.Database.CanConnectAsync(token) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    catch { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
}).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static async Task BootstrapFirstPlatformAdminAsync(IServiceProvider services)
{
    Console.Write("Correo del administrador inicial: ");
    var email = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(email) || !System.Net.Mail.MailAddress.TryCreate(email, out _))
        throw new InvalidOperationException("Escribe un correo válido.");
    Console.Write("Contraseña (12+ caracteres; entrada oculta): ");
    var chars = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key is ConsoleKey.Enter) break;
        if (key.Key is ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
        if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }
    Console.WriteLine();
    var password = new string(chars.ToArray());
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sp_getapplock @Resource = 'Citalu.PlatformAdminBootstrap', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50001, 'No se pudo bloquear la creación inicial del administrador.', 1;");
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var role = await roles.FindByNameAsync(PlatformSubscriptionAdminController.AdminRole);
    if (role is not null && (await users.GetUsersInRoleAsync(PlatformSubscriptionAdminController.AdminRole)).Count > 0)
        throw new InvalidOperationException("Ya existe un administrador de plataforma; el alta inicial se deshabilitó.");
    if (await users.FindByEmailAsync(email) is not null)
        throw new InvalidOperationException("Ese correo ya tiene cuenta. Usa un correo nuevo para la creación inicial.");
    if (role is null)
    {
        var roleResult = await roles.CreateAsync(new IdentityRole(PlatformSubscriptionAdminController.AdminRole));
        if (!roleResult.Succeeded) throw new InvalidOperationException("No se pudo crear el rol de administración.");
    }
    var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
    var created = await users.CreateAsync(user, password);
    if (!created.Succeeded) throw new InvalidOperationException("No se pudo crear la cuenta inicial. Revisa los requisitos de contraseña.");
    var added = await users.AddToRoleAsync(user, PlatformSubscriptionAdminController.AdminRole);
    if (!added.Succeeded) throw new InvalidOperationException("No se pudo asignar el rol de plataforma.");
    await tx.CommitAsync();
    Console.WriteLine("Administrador inicial creado. La contraseña no se guardó en la configuración.");
}

static void ValidateOperationalConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (environment.IsDevelopment()) return;
    var missing = new List<string>();
    var connection = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connection)) missing.Add("ConnectionStrings:DefaultConnection");
    else if (connection.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Production requires a SQL Server instance; LocalDB is not supported.");

    var publicUrl = configuration["Application:PublicBaseUrl"];
    if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
        !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        missing.Add("Application:PublicBaseUrl (absolute HTTPS canonical URL)");
    var allowedHosts = configuration["AllowedHosts"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    if (allowedHosts.Length == 0 || allowedHosts.Contains("*", StringComparer.Ordinal) ||
        (Uri.TryCreate(publicUrl, UriKind.Absolute, out var canonical) && !allowedHosts.Contains(canonical.Host, StringComparer.OrdinalIgnoreCase)))
        missing.Add("AllowedHosts (explicit host matching Application:PublicBaseUrl)");

    if (!Path.IsPathFullyQualified(configuration["DataProtection:KeysPath"] ?? string.Empty)) missing.Add("DataProtection:KeysPath (absolute persistent protected storage path)");
    if (!Path.IsPathFullyQualified(configuration["DataProtection:CertificatePath"] ?? string.Empty)) missing.Add("DataProtection:CertificatePath (PFX with private key outside Git)");
    if (string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePassword"])) missing.Add("DataProtection:CertificatePassword (secret configuration)");
    if (!Path.IsPathFullyQualified(configuration["Storage:UploadsPath"] ?? string.Empty)) missing.Add("Storage:UploadsPath (absolute persistent file storage path)");
    var demoCaptureOnly = environment.IsEnvironment("Demo") && configuration.GetValue<bool>("Email:CaptureOnly");
    if (demoCaptureOnly)
    {
        var capturePath = configuration["Email:CapturePath"];
        if (!Path.IsPathFullyQualified(capturePath ?? string.Empty))
            missing.Add("Email:CapturePath (absolute private path outside the published web root)");
        else
        {
            var webRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot"));
            if (OperationalPathValidator.IsInsideWebRoot(webRoot, capturePath!))
                missing.Add("Email:CapturePath (must be outside the published web root)");
        }
    }
    else
    {
        if (string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"])) missing.Add("Email:Smtp:Host");
        if (!int.TryParse(configuration["Email:Smtp:Port"], out var port) || port is < 1 or > 65535) missing.Add("Email:Smtp:Port");
        if (string.IsNullOrWhiteSpace(configuration["Email:Smtp:From"])) missing.Add("Email:Smtp:From");
        if (!string.IsNullOrWhiteSpace(configuration["Email:Smtp:Username"]) && string.IsNullOrWhiteSpace(configuration["Email:Smtp:Password"]))
            missing.Add("Email:Smtp:Password (required when username is set)");
        if (!configuration.GetValue<bool>("Email:Smtp:EnableSsl")) missing.Add("Email:Smtp:EnableSsl=true");
    }

    if (configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    {
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        if (proxies.Length == 0 || proxies.Any(address => !IPAddress.TryParse(address, out _)))
            missing.Add("ForwardedHeaders:KnownProxies (valid trusted proxy IP addresses)");
    }

    if (missing.Count > 0)
        throw new InvalidOperationException("Non-development configuration is incomplete or unsafe. Configure: " + string.Join(", ", missing));
}

// Expuesto para que las pruebas de integración puedan iniciar el host más adelante.
public partial class Program { }
