using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace ValidasiTugasAkhir.MainService.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SttsDbContext sttsDb, IConfiguration configuration)
    {
        var path = context.Request.Path.Value?.ToLower();
        
        if (path == "/api/auth/login" || path == "/api/auth/refresh" || path?.Contains("/api/testing") == true || path?.Contains("/api/rules") == true || path == "/ws" || path == "/" || path == "/health")
        {
            await _next(context);
            return;
        }

        var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrEmpty(token))
            token = context.Request.Query["token"].ToString();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Auth rejected for {Path}: token missing", path);
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"Token tidak ditemukan\"}");
            return;
        }

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = configuration["Auth:JwtSecret"];
            if (string.IsNullOrEmpty(secret))
            {
                _logger.LogError("Auth failed for {Path}: JWT secret is not configured", path);
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\":\"JWT Secret tidak dikonfigurasi\"}");
                return;
            }
            
            var key = Encoding.UTF8.GetBytes(secret);

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var claimsIdentity = new ClaimsIdentity(jwtToken.Claims, "Jwt");
            context.User = new ClaimsPrincipal(claimsIdentity);

            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == "username")?.Value;
            var role = jwtToken.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("Auth rejected for {Path}: username claim missing", path);
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\":\"Username tidak ditemukan dalam token\"}");
                return;
            }

            if (role == "admin")
            {
                context.Items["Username"] = username;
                context.Items["Nrp"] = username;
                context.Items["Role"] = role;
            }
            else
            {
                var mahasiswa = await sttsDb.Mahasiswas
                    .Where(m => m.MhsNrp == username)
                    .Select(m => new { m.MhsNrp })
                    .FirstOrDefaultAsync();

                if (mahasiswa == null)
                {
                    _logger.LogWarning("Auth rejected for {Path}: mahasiswa {Username} not found", path, username);
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"message\":\"Mahasiswa tidak ditemukan\"}");
                    return;
                }

                context.Items["Username"] = username;
                context.Items["Nrp"] = username;
                context.Items["Role"] = role ?? "mahasiswa";
            }
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            _logger.LogWarning(ex, "Auth rejected for {Path}: token validation failed", path);
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"Token tidak valid\"}");
            return;
        }

        await _next(context);
    }
}
