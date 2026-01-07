using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidasiTugasAkhir.MainService.Services;

namespace ValidasiTugasAkhir.MainService.Controllers;

[ApiController]
public class WebSocketController : ControllerBase
{
    private readonly IWebSocketService _wsService;

    public WebSocketController(IWebSocketService wsService)
    {
        _wsService = wsService;
    }

    [Route("/ws")]
    public async Task Get([FromServices] IConfiguration configuration, [FromServices] SttsDbContext sttsDb)
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            var token = HttpContext.Request.Query["token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                HttpContext.Response.StatusCode = 401;
                await HttpContext.Response.WriteAsync("{\"message\":\"Token required\"}");
                return;
            }

            // Validate token first, before accepting WebSocket
            string? username = null;
            try
            {
                var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var secret = configuration["Auth:JwtSecret"];
                if (string.IsNullOrEmpty(secret))
                {
                    HttpContext.Response.StatusCode = 500;
                    return;
                }
                var key = System.Text.Encoding.UTF8.GetBytes(secret);

                tokenHandler.ValidateToken(token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                }, out var validatedToken);

                var jwtToken = (System.IdentityModel.Tokens.Jwt.JwtSecurityToken)validatedToken;
                username = jwtToken.Claims.FirstOrDefault(c => c.Type == "username")?.Value;
            }
            catch
            {
                HttpContext.Response.StatusCode = 401;
                await HttpContext.Response.WriteAsync("{\"message\":\"Invalid token\"}");
                return;
            }

            if (string.IsNullOrEmpty(username))
            {
                HttpContext.Response.StatusCode = 401;
                return;
            }

            var mahasiswa = await sttsDb.Mahasiswas.FirstOrDefaultAsync(m => m.MhsNrp == username);
            if (mahasiswa == null)
            {
                HttpContext.Response.StatusCode = 401;
                return;
            }

            // Accept WebSocket only after all validation passes
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await _wsService.HandleWebSocketAsync(webSocket, mahasiswa.MhsNrp);
        }
        else
        {
            HttpContext.Response.StatusCode = 400;
        }
    }
}
