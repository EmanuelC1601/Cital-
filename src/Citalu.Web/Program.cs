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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
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
builder.Services.AddScoped<IEmailSender>(services => builder.Environment.IsDevelopment()
    ? services.GetRequiredService<CapturedEmailSender>()
    : services.GetRequiredService<SmtpEmailSender>());
builder.Services.AddScoped<CapturedEmailSender>();
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddHostedService<OutboxProcessor>();
builder.Services.AddHostedService<SubscriptionExpiryNotifier>();
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
if (builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(dataProtectionPath))
{
    // Claves fuera de Git: mantienen válidos los tokens entre reinicios de desarrollo.
    var keyPath = dataProtectionPath ?? Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
    Directory.CreateDirectory(keyPath);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

var app = builder.Build();

if (args.Contains("--create-platform-admin", StringComparer.Ordinal))
{
    await BootstrapFirstPlatformAdminAsync(app.Services);
    return;
}

// Configure the HTTP request pipeline.
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
app.MapStaticAssets();

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

// Expuesto para que las pruebas de integración puedan iniciar el host más adelante.
public partial class Program { }
