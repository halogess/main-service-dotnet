using _.Services;
using ValidasiTugasAkhir.MainService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);



// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Konfigurasi CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173", "http://localhost:8000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Konfigurasi DbContext STTS 
var sttsConnectionString = builder.Configuration.GetConnectionString("SttsDbConnection");
var sttsServerVersion = new MySqlServerVersion(new Version(8, 0, 34));
builder.Services.AddDbContext<SttsDbContext>(options =>
    options.UseMySql(sttsConnectionString, sttsServerVersion, 
        mysqlOptions => mysqlOptions.EnableRetryOnFailure()));

// Konfigurasi DbContext KorektorBuku
var korektorBukuConnectionString = builder.Configuration.GetConnectionString("KorektorBukuDbConnection");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 34)); 
builder.Services.AddDbContext<KorektorBukuDbContext>(options =>
    options.UseMySql(korektorBukuConnectionString, serverVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure()));

// Register HttpClient
builder.Services.AddHttpClient();

// Register services
builder.Services.AddScoped<IMahasiswaService, MahasiswaService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IDokumenService, DokumenService>();
builder.Services.AddScoped<IBukuService, BukuService>();
builder.Services.AddSingleton<IWebSocketService, WebSocketService>();

// Register Background Service
builder.Services.AddHostedService<AdobeQuotaResetService>();
builder.Services.AddScoped<IPdfConversionService, PdfConversionService>();

// Register background service
builder.Services.AddHostedService<PdfQueueBackgroundService>();

var app = builder.Build();

// Ensure storage directory exists
var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH") ?? "/app/storage";
Directory.CreateDirectory(storagePath);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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
