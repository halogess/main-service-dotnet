using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;

namespace _.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, SttsDbContext sttsDb)
    {
        var path = context.Request.Path.Value?.ToLower();
        Console.WriteLine($"[AUTH] Middleware called for path: {path}");
        
        // Skip auth untuk endpoint login
        if (path == "/api/auth/login" || path == "/api/auth/refresh")
        {
            Console.WriteLine($"[AUTH] Skipping auth for: {path}");
            await _next(context);
            return;
        }

        var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { message = "Token tidak ditemukan" });
            return;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            
            Console.WriteLine($"[AUTH] All claims: {string.Join(", ", jwtToken.Claims.Select(c => $"{c.Type}={c.Value}"))}");
            
            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == "username")?.Value;
            Console.WriteLine($"[AUTH] Username from token: {username}");

            if (string.IsNullOrEmpty(username))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Username tidak ditemukan dalam token" });
                return;
            }

            var mahasiswa = await sttsDb.Mahasiswas
                .FirstOrDefaultAsync(m => m.MhsNrp == username);
            
            Console.WriteLine($"[AUTH] Mahasiswa found: {mahasiswa != null}, NRP: {mahasiswa?.MhsNrp}");

            if (mahasiswa == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Mahasiswa tidak ditemukan" });
                return;
            }

            context.Items["Username"] = username;
            context.Items["Nrp"] = mahasiswa.MhsNrp;
            
            Console.WriteLine($"[AUTH] Saved to context - Username: {context.Items["Username"]}, Nrp: {context.Items["Nrp"]}");

            await _next(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTH] Error: {ex.Message}");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { message = "Token tidak valid" });
        }
    }
}
