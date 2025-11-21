using Microsoft.AspNetCore.Mvc;
using _.Services;

namespace _.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            Console.WriteLine("[HOT RELOAD TEST] Login endpoint hit - Hot reload is working! 🔥");
            
            if (string.IsNullOrEmpty(request.username) || string.IsNullOrEmpty(request.password))
            {
                return BadRequest(new { message = "Username dan password harus diisi" });
            }

            var result = await _authService.Login(request.username, request.password);
            
            if (result == null)
            {
                return Unauthorized(new { message = "Username atau password salah" });
            }

            return Ok(new 
            {
                access_token = ((dynamic)result).access_token,
                refresh_token = ((dynamic)result).refresh_token
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
            var refreshToken = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            
            if (string.IsNullOrEmpty(refreshToken))
            {
                return BadRequest(new { message = "Refresh token harus disertakan di header Authorization" });
            }

            var result = await _authService.RefreshToken(refreshToken);
            
            if (result == null)
            {
                return Unauthorized(new { message = "Refresh token tidak valid atau sudah expired" });
            }

            return Ok(new 
            {
                access_token = ((dynamic)result).access_token,
                refresh_token = ((dynamic)result).refresh_token
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Terjadi kesalahan pada server", error = ex.Message });
        }
    }
}

public class LoginRequest
{
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
}
