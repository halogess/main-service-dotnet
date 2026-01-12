using Microsoft.AspNetCore.Mvc;
using _.Services;

namespace _.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthService authService, IConfiguration configuration, IWebHostEnvironment env)
    {
        _authService = authService;
        _configuration = configuration;
        _env = env;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.username) || string.IsNullOrEmpty(request.password))
                return BadRequest(new { message = "Username dan password harus diisi" });

            var result = await _authService.Login(request.username, request.password);
            if (result == null)
                return Unauthorized(new { message = "Username atau password salah" });

            var refreshToken = ((dynamic)result).refresh_token;
            SetRefreshTokenCookie(refreshToken);

            return Ok(new 
            {
                access_token = ((dynamic)result).access_token
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Terjadi kesalahan pada server", error = ex.Message });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        try
        {
            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrEmpty(refreshToken))
                return BadRequest(new { message = "Refresh token harus ada di cookie" });

            var result = await _authService.RefreshToken(refreshToken);
            if (result == null)
                return Unauthorized(new { message = "Refresh token tidak valid atau sudah expired" });

            var newRefreshToken = ((dynamic)result).refresh_token;
            SetRefreshTokenCookie(newRefreshToken);

            return Ok(new 
            {
                access_token = ((dynamic)result).access_token
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Terjadi kesalahan pada server", error = ex.Message });
        }
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        try
        {
            var user = HttpContext.Items["User"] as dynamic;
            if (user == null)
            {
                // Fallback to claims if middleware hasn't set Items["User"] but validation passed
                var username = User.FindFirst("username")?.Value ?? User.Identity?.Name;
                var role = User.FindFirst("role")?.Value;
                
                if (username == null)
                    return Unauthorized(new { message = "User tidak terautentikasi" });

                return Ok(new
                {
                    username = username,
                    nama = User.FindFirst("nama")?.Value ?? username,
                    role = role
                });
            }

            return Ok(new
            {
                username = user.Username,
                nama = user.Nama ?? user.Username, // Handle potential null 'Nama'
                role = user.Role
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Terjadi kesalahan pada server", error = ex.Message });
        }
    }

    private void SetRefreshTokenCookie(string token)
    {
        var expiryDays = _configuration.GetValue<int>("Auth:RefreshTokenExpiryDays", 7);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Expires = DateTime.UtcNow.AddDays(expiryDays),
            SameSite = SameSiteMode.Lax,
            Secure = !_env.IsDevelopment()
        };
        Response.Cookies.Append("refresh_token", token, cookieOptions);
    }
}

public class LoginRequest
{
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
}
