using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace _.Services;

public class AuthService : IAuthService
{
    private readonly SttsDbContext _sttsDbContext;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public AuthService(SttsDbContext sttsDbContext, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _sttsDbContext = sttsDbContext;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<object?> Login(string username, string password)
    {
        // 1. Validasi ke API external
        var isValid = await ValidateExternalApi(username, password);
        if (!isValid) return null;

        // 2. Cek apakah admin
        var adminUsername = _configuration["Auth:AdminUsername"];
        if (username == adminUsername)
        {
            return GenerateTokens(username, "Administrator", "admin");
        }

        // 3. Cari mahasiswa berdasarkan NRP
        var mahasiswa = await _sttsDbContext.Mahasiswas
            .FirstOrDefaultAsync(m => m.MhsNrp == username);
        
        if (mahasiswa == null) return null;

        return GenerateTokens(username, mahasiswa.MhsNama ?? username, "mahasiswa");
    }

    public async Task<object?> RefreshToken(string refreshToken)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = _configuration["Auth:JwtSecret"]!;
            var key = Encoding.UTF8.GetBytes(secret);

            tokenHandler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var username = jwtToken.Claims.First(x => x.Type == "username").Value;

            // Query ulang untuk mendapatkan data terbaru
            var adminUsername = _configuration["Auth:AdminUsername"];
            if (username == adminUsername)
            {
                return GenerateTokens(username, "Administrator", "admin");
            }

            var mahasiswa = await _sttsDbContext.Mahasiswas
                .FirstOrDefaultAsync(m => m.MhsNrp == username);
            
            if (mahasiswa == null) return null;

            return GenerateTokens(username, mahasiswa.MhsNama ?? username, "mahasiswa");
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ValidateExternalApi(string username, string password)
    {
        try
        {
            var baseUrl = _configuration["Auth:ExternalApiUrl"];
            var token = _configuration["Auth:ExternalApiToken"];
            var appName = _configuration["Auth:AppName"];
            var url = $"{baseUrl}/{username}/login/{password}&appname={appName}";
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("token", token);
            
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private object GenerateTokens(string username, string nama, string role)
    {
        var secret = _configuration["Auth:JwtSecret"]!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var accessTokenClaims = new[]
        {
            new Claim("username", username),
            new Claim("nama", nama),
            new Claim("role", role)
        };

        var refreshTokenClaims = new[]
        {
            new Claim("username", username)
        };

        var accessTokenExpiry = DateTime.Now.AddMinutes(int.Parse(_configuration["Auth:AccessTokenExpiryMinutes"]!));
        var accessToken = new JwtSecurityToken(
            claims: accessTokenClaims,
            expires: accessTokenExpiry,
            signingCredentials: credentials
        );

        var refreshTokenExpiry = DateTime.Now.AddDays(int.Parse(_configuration["Auth:RefreshTokenExpiryDays"]!));
        var refreshToken = new JwtSecurityToken(
            claims: refreshTokenClaims,
            expires: refreshTokenExpiry,
            signingCredentials: credentials
        );

        return new
        {
            access_token = new JwtSecurityTokenHandler().WriteToken(accessToken),
            refresh_token = new JwtSecurityTokenHandler().WriteToken(refreshToken)
        };
    }
}
