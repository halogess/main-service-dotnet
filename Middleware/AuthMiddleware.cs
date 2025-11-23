using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ValidasiTugasAkhir.MainService.Middleware;

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
        
        // Skip auth untuk endpoint login, websocket, testing, dan health check
        if (path == "/api/auth/login" || path == "/api/auth/refresh" || path?.StartsWith("/api/testing") == true || path == "/ws" || path == "/" || path == "/health")
        {
            Console.WriteLine($"[AUTH] Skipping auth for: {path}");
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
            Console.WriteLine($"[AUTH] Secret (first 10 chars): {secret.Substring(0, Math.Min(10, secret.Length))}...");
            
            // Decode token tanpa validasi untuk debug
            var jwtHandler = new JwtSecurityTokenHandler();
            var decodedToken = jwtHandler.ReadJwtToken(token);
            Console.WriteLine($"[AUTH] Token claims: {string.Join(", ", decodedToken.Claims.Select(c => $"{c.Type}={c.Value}"))}");
            Console.WriteLine($"[AUTH] Token expires: {decodedToken.ValidTo}");
            
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
                    .Where(m => m.MhsNrp == username)
                    .Select(m => new { m.MhsNrp, m.MhsNama })
                    .FirstOrDefaultAsync();
                
                Console.WriteLine($"[AUTH] Mahasiswa found: {mahasiswa != null}, NRP: {mahasiswa?.MhsNrp}");

                if (mahasiswa == null)
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"message\":\"Mahasiswa tidak ditemukan\"}");
                    return;
                }

                context.Items["Username"] = username;
                context.Items["Nrp"] = username;
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
