using _.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

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

// Register services
builder.Services.AddScoped<IMahasiswaService, MahasiswaService>();

var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
