using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace _.Services;

public class AuthService : IAuthService
{
    private readonly SttsDbContext _sttsDbContext;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        SttsDbContext sttsDbContext,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<AuthService> logger)
    {
        _sttsDbContext = sttsDbContext;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<object?> Login(string username, string password)
    {
        var normalizedUsername = username?.Trim() ?? string.Empty;
        _logger.LogInformation("Login attempt started for {Username}", normalizedUsername);
        var trace = await TraceLogin(normalizedUsername, password);
        if (!trace.Success)
        {
            _logger.LogWarning(
                "Login failed for {Username}. Branch={Branch}, ExternalStatus={StatusCode}, ExternalParsed={Parsed}, ExternalAccepted={Accepted}, MahasiswaFound={MahasiswaFound}",
                trace.NormalizedUsername,
                trace.NullBranch,
                trace.ExternalAuthHttpStatus,
                trace.ExternalAuthParsed,
                trace.ExternalAuthAccepted,
                trace.MahasiswaFound);
            return null;
        }

        _logger.LogInformation("Login succeeded for {Username} as {Role}", trace.NormalizedUsername, trace.Role);
        return GenerateTokens(trace.NormalizedUsername, trace.DisplayName ?? trace.NormalizedUsername, trace.Role ?? "mahasiswa");
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
        var trace = await ValidateExternalApiWithTrace(username, password);
        return trace.Accepted;
    }

    private async Task<ExternalAuthTrace> ValidateExternalApiWithTrace(string username, string password)
    {
        var trace = new ExternalAuthTrace();

        try
        {
            var baseUrl = _configuration["Auth:ExternalApiUrl"];
            var token = _configuration["Auth:ExternalApiToken"];
            var appName = _configuration["Auth:AppName"];

            if (string.IsNullOrWhiteSpace(baseUrl) ||
                string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(appName))
            {
                _logger.LogWarning("External auth configuration is incomplete");
                trace.ResponsePreview = "External auth configuration is incomplete";
                return trace;
            }

            var url = BuildExternalAuthUrl(baseUrl, username, password, appName);
            trace.UrlPreview = BuildExternalAuthUrl(baseUrl, username, "***", appName);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("token", token);

            using var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            trace.StatusCode = (int)response.StatusCode;
            trace.ResponsePreview = PreviewResponse(responseBody);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "External auth request failed for {Username} with status code {StatusCode}. Body preview: {BodyPreview}",
                    username,
                    trace.StatusCode,
                    trace.ResponsePreview);
                return trace;
            }

            trace.Parsed = TryParseExternalAuthResponse(responseBody, out var isValid);
            trace.Accepted = trace.Parsed && isValid;

            if (!trace.Parsed)
            {
                _logger.LogWarning(
                    "External auth returned an unrecognized response for {Username}. Body preview: {BodyPreview}",
                    username,
                    trace.ResponsePreview);
            }

            return trace;
        }
        catch (Exception ex)
        {
            trace.ResponsePreview = ex.Message;
            _logger.LogWarning(ex, "External auth request threw an exception for {Username}", username);
            return trace;
        }
    }

    public async Task<AuthLoginTrace> TraceLogin(string username, string password)
    {
        var trace = new AuthLoginTrace
        {
            InputUsername = username,
            NormalizedUsername = username.Trim()
        };

        try
        {
            var externalTrace = await ValidateExternalApiWithTrace(trace.NormalizedUsername, password);
            trace.ExternalAuthUrlPreview = externalTrace.UrlPreview;
            trace.ExternalAuthHttpStatus = externalTrace.StatusCode;
            trace.ExternalAuthResponsePreview = externalTrace.ResponsePreview;
            trace.ExternalAuthParsed = externalTrace.Parsed;
            trace.ExternalAuthAccepted = externalTrace.Accepted;
            _logger.LogInformation(
                "External auth evaluated for {Username}. StatusCode={StatusCode}, Parsed={Parsed}, Accepted={Accepted}, UrlPreview={UrlPreview}, BodyPreview={BodyPreview}",
                trace.NormalizedUsername,
                trace.ExternalAuthHttpStatus,
                trace.ExternalAuthParsed,
                trace.ExternalAuthAccepted,
                trace.ExternalAuthUrlPreview,
                trace.ExternalAuthResponsePreview);

            if (!externalTrace.Accepted)
            {
                trace.NullBranch = "external_auth_rejected";
                _logger.LogWarning("Login rejected by external auth for {Username}", trace.NormalizedUsername);
                return trace;
            }

            _logger.LogInformation("External auth accepted {Username}", trace.NormalizedUsername);

            trace.AdminUsername = _configuration["Auth:AdminUsername"]?.Trim();
            trace.IsAdmin = string.Equals(trace.NormalizedUsername, trace.AdminUsername, StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation(
                "Login role evaluation for {Username}. AdminUsername={AdminUsername}, IsAdmin={IsAdmin}",
                trace.NormalizedUsername,
                trace.AdminUsername,
                trace.IsAdmin);
            if (trace.IsAdmin)
            {
                trace.Success = true;
                trace.Role = "admin";
                trace.DisplayName = "Administrator";
                _logger.LogInformation("Issuing admin token for {Username}", trace.NormalizedUsername);
                return trace;
            }

            var mahasiswa = await _sttsDbContext.Mahasiswas
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.MhsNrp == trace.NormalizedUsername);

            if (mahasiswa == null)
            {
                trace.MahasiswaFound = false;
                trace.NullBranch = "mahasiswa_not_found";
                _logger.LogWarning(
                    "External auth succeeded but mahasiswa {Username} was not found in the local STTS database",
                    trace.NormalizedUsername);
                return trace;
            }

            trace.MahasiswaFound = true;
            trace.DisplayName = mahasiswa.MhsNama ?? trace.NormalizedUsername;
            trace.Role = "mahasiswa";
            trace.Success = true;
            _logger.LogInformation(
                "Mahasiswa lookup succeeded for {Username}. DisplayName={DisplayName}",
                trace.NormalizedUsername,
                trace.DisplayName);
            _logger.LogInformation("Issuing mahasiswa token for {Username}", trace.NormalizedUsername);
            return trace;
        }
        catch (Exception ex)
        {
            trace.NullBranch = "unexpected_exception";
            trace.ExceptionMessage = ex.Message;
            _logger.LogWarning(ex, "Unexpected login trace failure for {Username}", trace.NormalizedUsername);
            return trace;
        }
    }

    internal static string BuildExternalAuthUrl(string baseUrl, string username, string password, string appName)
    {
        return $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(username)}/login/{Uri.EscapeDataString(password)}&appname={Uri.EscapeDataString(appName)}";
    }

    internal static bool TryParseExternalAuthResponse(string responseBody, out bool isValid)
    {
        var trimmed = responseBody.Trim();

        if (bool.TryParse(trimmed, out isValid))
            return true;

        if (trimmed == "1")
        {
            isValid = true;
            return true;
        }

        if (trimmed == "0")
        {
            isValid = false;
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (TryReadBoolean(root, out isValid))
                return true;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("response", out var responseElement) &&
                    TryReadBoolean(responseElement, out isValid))
                {
                    return true;
                }

                if (root.TryGetProperty("success", out var successElement) &&
                    TryReadBoolean(successElement, out isValid))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        isValid = false;
        return false;
    }

    private static bool TryReadBoolean(JsonElement element, out bool isValid)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                isValid = true;
                return true;
            case JsonValueKind.False:
                isValid = false;
                return true;
            case JsonValueKind.String:
                var stringValue = element.GetString();
                if (bool.TryParse(stringValue, out isValid))
                    return true;

                if (stringValue == "1")
                {
                    isValid = true;
                    return true;
                }

                if (stringValue == "0")
                {
                    isValid = false;
                    return true;
                }

                break;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var numericValue) && (numericValue == 0 || numericValue == 1))
                {
                    isValid = numericValue == 1;
                    return true;
                }

                break;
        }

        isValid = false;
        return false;
    }

    private static string PreviewResponse(string responseBody)
    {
        const int maxLength = 120;
        var normalized = responseBody.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private sealed class ExternalAuthTrace
    {
        public bool Accepted { get; set; }
        public bool Parsed { get; set; }
        public int? StatusCode { get; set; }
        public string? ResponsePreview { get; set; }
        public string? UrlPreview { get; set; }
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

public class AuthLoginTrace
{
    public string InputUsername { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string? AdminUsername { get; set; }
    public bool ExternalAuthParsed { get; set; }
    public bool ExternalAuthAccepted { get; set; }
    public int? ExternalAuthHttpStatus { get; set; }
    public string? ExternalAuthUrlPreview { get; set; }
    public string? ExternalAuthResponsePreview { get; set; }
    public bool IsAdmin { get; set; }
    public bool? MahasiswaFound { get; set; }
    public bool Success { get; set; }
    public string? Role { get; set; }
    public string? DisplayName { get; set; }
    public string? NullBranch { get; set; }
    public string? ExceptionMessage { get; set; }
}
