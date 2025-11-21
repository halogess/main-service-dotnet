using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace _.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, SttsDbContext sttsDb, IConfiguration configuration)
    {
        var path = context.Request.Path.Value?.ToLower();
        Console.WriteLine($"[AUTH] Middleware called for path: {path}");
        
        // Skip auth untuk endpoint login, internal, dan websocket
        if (path == "/api/auth/login" || path == "/api/auth/refresh" || path?.StartsWith("/api/internal") == true || path == "/ws")
        {
            Console.WriteLine($"[AUTH] Skipping auth for: {path}");
            await _next(context);
            return;
        }

        // Skip JWT validation jika ada X-API-Key (untuk AI service)
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine($"[AUTH] X-API-Key detected, skipping JWT validation");
            await _next(context);
            return;
        }

        var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        
        // Fallback untuk WebSocket: ambil token dari query string
        if (string.IsNullOrEmpty(token))
        {
            token = context.Request.Query["token"].ToString();
        }

        if (string.IsNullOrEmpty(token))
        {
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
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\":\"JWT Secret tidak dikonfigurasi\"}");
                return;
            }
            Console.WriteLine($"[AUTH] Secret length: {secret.Length}, Token length: {token.Length}");
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
            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == "username")?.Value;
            var role = jwtToken.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
            
            Console.WriteLine($"[AUTH] Username from token: {username}, Role: {role}");

            if (string.IsNullOrEmpty(username))
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\":\"Username tidak ditemukan dalam token\"}");
                return;
            }

            // Jika admin, skip validasi mahasiswa
            if (role == "admin")
            {
                context.Items["Username"] = username;
                context.Items["Nrp"] = username;
                context.Items["Role"] = role;
                Console.WriteLine($"[AUTH] Admin access granted: {username}");
            }
            else
            {
                var mahasiswa = await sttsDb.Mahasiswas
                    .FirstOrDefaultAsync(m => m.MhsNrp == username);
                
                Console.WriteLine($"[AUTH] Mahasiswa found: {mahasiswa != null}, NRP: {mahasiswa?.MhsNrp}");

                if (mahasiswa == null)
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"message\":\"Mahasiswa tidak ditemukan\"}");
                    return;
                }

                context.Items["Username"] = username;
                context.Items["Nrp"] = mahasiswa.MhsNrp;
                context.Items["Role"] = role ?? "mahasiswa";
            }
            
            Console.WriteLine($"[AUTH] Saved to context - Username: {context.Items["Username"]}, Nrp: {context.Items["Nrp"]}, Role: {context.Items["Role"]}");

            await _next(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTH] Error: {ex.Message}");
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"Token tidak valid\"}");
        }
    }
}
