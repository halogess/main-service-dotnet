using _.Services;
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
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Konfigurasi DbContext STTS 
var sttsConnectionString = builder.Configuration.GetConnectionString("SttsDbConnection");
var sttsServerVersion = new MySqlServerVersion(new Version(8, 0, 34));
builder.Services.AddDbContext<SttsDbContext>(options =>
    options.UseMySql(sttsConnectionString, sttsServerVersion));

// Konfigurasi DbContext KorektorBuku
var korektorBukuConnectionString = builder.Configuration.GetConnectionString("KorektorBukuDbConnection");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 34)); 
builder.Services.AddDbContext<KorektorBukuDbContext>(options =>
    options.UseMySql(korektorBukuConnectionString, serverVersion));

// Register HttpClient
builder.Services.AddHttpClient();

// Register services
builder.Services.AddScoped<IMahasiswaService, MahasiswaService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IDokumenService, DokumenService>();
builder.Services.AddScoped<IBukuService, BukuService>();

var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

app.UseHttpsRedirection();

app.UseMiddleware<_.Middleware.AuthMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
