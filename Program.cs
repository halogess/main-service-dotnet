using _.Services;
using ValidasiTugasAkhir.MainService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

LoadDotEnvIfPresent();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = null;
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .ToArray()
    ?? ["http://localhost:5173", "https://localhost:5173", "http://localhost:8000"];

// Konfigurasi CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Konfigurasi DbContext
var serverVersion = new MySqlServerVersion(new Version(8, 0, 34));
builder.Services.AddDbContext<SttsDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("SttsDbConnection"), serverVersion, 
        mysqlOptions => mysqlOptions.EnableRetryOnFailure()));

builder.Services.AddDbContext<KorektorBukuDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("KorektorBukuDbConnection"), serverVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure()));

// Register HttpClient
builder.Services.AddHttpClient();

// Register services
builder.Services.AddScoped<IMahasiswaService, MahasiswaService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IDokumenService, DokumenService>();
builder.Services.AddScoped<IDokumenImportService, DokumenImportService>();
builder.Services.AddScoped<IDokumenHistoryPurgeService, DokumenHistoryPurgeService>();
builder.Services.AddScoped<IExtractionArtifactCleanupService, ExtractionArtifactCleanupService>();
builder.Services.AddScoped<INonActiveBookHistoryPurgeService, NonActiveBookHistoryPurgeService>();
builder.Services.AddScoped<IBukuService, BukuService>();
builder.Services.AddScoped<IBukuArchiveService, BukuArchiveService>();
builder.Services.AddScoped<IDocxExtractionService, DocxExtractionService>();
builder.Services.AddScoped<IAturanService, AturanService>();
builder.Services.AddScoped<IAturanExcelImportPreviewService, AturanExcelImportPreviewService>();
builder.Services.AddScoped<IAturanImportService, AturanImportService>();
builder.Services.AddScoped<IJurusanService, JurusanService>();
builder.Services.AddScoped<IValidationService, ValidationService>();
builder.Services.AddScoped<IValidationReportService, ValidationReportService>();
builder.Services.AddScoped<IHighlightedTextExtractor, HighlightedTextExtractor>();
builder.Services.AddSingleton<IWebSocketService, WebSocketService>();

// Register Background Services
builder.Services.AddScoped<IPdfConversionService, PdfConversionService>();
builder.Services.AddHostedService<AdobeQuotaResetService>();
builder.Services.AddHostedService<PdfQueueBackgroundService>();
builder.Services.AddHostedService<ValidationQueueBackgroundService>();
builder.Services.AddHostedService<DokumenAutoDeleteService>();

// Register Gemini Service
builder.Services.AddHttpClient<IGeminiService, GeminiService>();

// Register Email Service
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

var app = builder.Build();

// Ensure storage directory exists
var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
Directory.CreateDirectory(storagePath);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();

app.UseCors("AllowFrontend");

app.UseWebSockets();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<ValidasiTugasAkhir.MainService.Middleware.AuthMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();

static void LoadDotEnvIfPresent()
{
    var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (!File.Exists(envPath))
        return;

    foreach (var rawLine in File.ReadLines(envPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            continue;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
            continue;

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        if (string.IsNullOrWhiteSpace(key))
            continue;

        if (Environment.GetEnvironmentVariable(key) == null)
            Environment.SetEnvironmentVariable(key, value);
    }
}
