using System.Net.Http.Headers;
using System.Text.Json;
using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IPdfConversionService
{
    Task<byte[]> ConvertDocxToPdfWithCredential(
        string docxFilePath,
        string clientId,
        string clientSecret,
        int? adobe_credentials_id = null,
        uint? antrian_id = null,
        CancellationToken cancellationToken = default);
}

public class PdfConversionService : IPdfConversionService
{
    private static readonly TimeSpan DefaultJobPollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultJobPollingTimeout = TimeSpan.FromMinutes(5);
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PdfConversionService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _jobPollingInterval;
    private readonly TimeSpan _jobPollingTimeout;
    private static readonly Dictionary<string, AdobeToken> _tokenCache = new();

    public PdfConversionService(IConfiguration configuration, HttpClient httpClient, ILogger<PdfConversionService> logger, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _jobPollingInterval = GetConfiguredPollingInterval(configuration);
        _jobPollingTimeout = GetConfiguredPollingTimeout(configuration);
    }

    public async Task<byte[]> ConvertDocxToPdfWithCredential(
        string docxFilePath,
        string clientId,
        string clientSecret,
        int? adobe_credentials_id = null,
        uint? antrian_id = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Memulai konversi DOCX ke PDF: {FilePath}, Antrian ID: {AntrianId}", docxFilePath, antrian_id);
        cancellationToken.ThrowIfCancellationRequested();

        var apiBaseUrl = _configuration["Adobe:ApiBaseUrl"];
        var tokenUrl = BuildAdobeTokenUrl(apiBaseUrl);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tokenUrl) || string.IsNullOrEmpty(apiBaseUrl))
        {
            throw new InvalidOperationException("Adobe credentials tidak ditemukan");
        }

        var cacheKey = $"{clientId}:{clientSecret}";
        if (!_tokenCache.ContainsKey(cacheKey) || _tokenCache[cacheKey].IsExpired)
        {
            _logger.LogInformation("Getting new access token from Adobe");
            var token = await GetAccessTokenWithExpiry(clientId, clientSecret, tokenUrl, antrian_id, cancellationToken);
            _tokenCache[cacheKey] = token;
        }
        else
        {
            _logger.LogInformation("Using cached access token");
        }

        var accessToken = _tokenCache[cacheKey].AccessToken;
        
        var (assetId, uploadUri) = await CreateUploadUri(accessToken, clientId, apiBaseUrl, adobe_credentials_id, antrian_id, cancellationToken);
        _logger.LogInformation("Upload dokumen ke Adobe: AssetID={AssetId}", assetId);
        await UploadDocument(uploadUri, docxFilePath, adobe_credentials_id, antrian_id, cancellationToken);
        
        var jobId = await CreateConversionJob(accessToken, clientId, assetId, apiBaseUrl, adobe_credentials_id, antrian_id, cancellationToken);
        _logger.LogInformation("Menunggu job selesai: {JobId}", jobId);
        var downloadUri = await PollJobStatus(accessToken, clientId, jobId, adobe_credentials_id, antrian_id, cancellationToken);
        
        var pdfBytes = await DownloadPdf(downloadUri, accessToken, adobe_credentials_id, antrian_id, cancellationToken);
        _logger.LogInformation("Konversi selesai: {Size} bytes", pdfBytes.Length);
        return pdfBytes;
    }

    private async Task<AdobeToken> GetAccessTokenWithExpiry(string clientId, string clientSecret, string tokenUrl, uint? antrian_id = null, CancellationToken cancellationToken = default)
    {
        var startTime = AppClock.Now;
        var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret)
        });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseTime = (int)(AppClock.Now - startTime).TotalMilliseconds;
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        
        await LogApiCall(null, tokenUrl, "POST", (int)response.StatusCode, responseTime, response.IsSuccessStatusCode ? null : responseBody, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken) ?? throw new InvalidOperationException("Failed to get access token");
        
        return new AdobeToken
        {
            AccessToken = result.access_token,
            ExpiresAt = AppClock.Now.AddSeconds(result.expires_in - 300)
        };
    }

    private class TokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public int expires_in { get; set; }
    }

    private async Task<(string assetId, string uploadUri)> CreateUploadUri(string accessToken, string clientId, string apiBaseUrl, int? adobe_credentials_id, uint? antrian_id = null, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{apiBaseUrl}/assets";
        var startTime = AppClock.Now;
        
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("x-api-key", clientId);
        request.Content = JsonContent.Create(new { mediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document" });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseTime = (int)(AppClock.Now - startTime).TotalMilliseconds;
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        
        await LogApiCall(adobe_credentials_id, endpoint, "POST", (int)response.StatusCode, responseTime, response.IsSuccessStatusCode ? null : responseBody, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken) ?? throw new InvalidOperationException("Failed to get asset response");
        return (result.AssetID, result.UploadUri);
    }

    private class AssetResponse
    {
        public string AssetID { get; set; } = string.Empty;
        public string UploadUri { get; set; } = string.Empty;
    }

    private async Task UploadDocument(string uploadUri, string filePath, int? adobe_credentials_id, uint? antrian_id = null, CancellationToken cancellationToken = default)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var startTime = AppClock.Now;
        
        var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        request.Content = new ByteArrayContent(fileBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseTime = (int)(AppClock.Now - startTime).TotalMilliseconds;
        
        await LogApiCall(adobe_credentials_id, "S3 Upload", "PUT", (int)response.StatusCode, responseTime, null, antrian_id);
        
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> CreateConversionJob(string accessToken, string clientId, string assetId, string apiBaseUrl, int? adobe_credentials_id, uint? antrian_id = null, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{apiBaseUrl}/operation/createpdf";
        var startTime = AppClock.Now;
        
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("x-api-key", clientId);
        request.Content = JsonContent.Create(new { assetID = assetId });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseTime = (int)(AppClock.Now - startTime).TotalMilliseconds;
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        
        await LogApiCall(adobe_credentials_id, endpoint, "POST", (int)response.StatusCode, responseTime, response.IsSuccessStatusCode ? null : responseBody, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var location = response.Headers.Location?.ToString() ?? throw new InvalidOperationException("Job location not found");
        return location;
    }

    private async Task<string> PollJobStatus(string accessToken, string clientId, string jobUri, int? adobe_credentials_id, uint? antrian_id = null, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow - startedAt > _jobPollingTimeout)
            {
                throw new TimeoutException($"Adobe job tidak selesai dalam {(int)_jobPollingTimeout.TotalSeconds} detik.");
            }

            var startTime = AppClock.Now;
            
            var request = new HttpRequestMessage(HttpMethod.Get, jobUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("x-api-key", clientId);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseTime = (int)(AppClock.Now - startTime).TotalMilliseconds;
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonElement? result = null;
            string? status = null;
            string? logErrorMessage = response.IsSuccessStatusCode ? null : responseBody;
            if (response.IsSuccessStatusCode)
            {
                result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                status = GetJsonString(result.Value, "status");
                var normalizedStatus = NormalizeJobStatus(status);
                if (IsTerminalFailureStatus(normalizedStatus))
                {
                    logErrorMessage = BuildAdobeJobStatusErrorMessage(status, ExtractAdobeErrorSummary(result.Value, responseBody));
                }
                else if (!string.IsNullOrWhiteSpace(normalizedStatus) && !IsActiveJobStatus(normalizedStatus) && !string.Equals(normalizedStatus, "done", StringComparison.Ordinal))
                {
                    logErrorMessage = $"Adobe job mengembalikan status tidak dikenal '{status}'.";
                }
            }
            
            await LogApiCall(adobe_credentials_id, jobUri, "GET", (int)response.StatusCode, responseTime, logErrorMessage, antrian_id);
            
            response.EnsureSuccessStatusCode();

            if (result == null)
            {
                throw new InvalidOperationException("Adobe job tidak mengembalikan respons status yang valid.");
            }

            var normalizedStatusValue = NormalizeJobStatus(status);
            if (string.IsNullOrWhiteSpace(normalizedStatusValue))
            {
                throw new InvalidOperationException($"Adobe job status tidak ditemukan. Respons: {TruncateErrorText(responseBody, 180)}");
            }

            if (string.Equals(normalizedStatusValue, "done", StringComparison.Ordinal))
            {
                if (!TryGetObjectProperty(result.Value, "asset", out var asset))
                {
                    throw new InvalidOperationException("Adobe job selesai tetapi asset hasil tidak ditemukan.");
                }

                var downloadUri = GetJsonString(asset, "downloadUri");
                if (string.IsNullOrWhiteSpace(downloadUri))
                {
                    throw new InvalidOperationException("Adobe job selesai tetapi downloadUri tidak ditemukan.");
                }

                return downloadUri;
            }

            if (IsTerminalFailureStatus(normalizedStatusValue))
            {
                throw new InvalidOperationException(BuildAdobeJobStatusErrorMessage(status, ExtractAdobeErrorSummary(result.Value, responseBody)));
            }

            if (!IsActiveJobStatus(normalizedStatusValue))
            {
                throw new InvalidOperationException(
                    $"Adobe job mengembalikan status tidak dikenal '{status}'. Respons: {TruncateErrorText(responseBody, 180)}");
            }

            await Task.Delay(_jobPollingInterval, cancellationToken);
        }
    }

    private async Task<byte[]> DownloadPdf(string downloadUri, string accessToken, int? adobe_credentials_id, uint? antrian_id = null, CancellationToken cancellationToken = default)
    {
        var startTime = AppClock.Now;
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseTime = (int)(AppClock.Now - startTime).TotalMilliseconds;
        
        await LogApiCall(adobe_credentials_id, "S3 Download", "GET", (int)response.StatusCode, responseTime, null, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var pdfBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return pdfBytes;
    }

    private async Task LogApiCall(int? adobe_credentials_id, string endpoint, string method, int status_code, int response_time_ms, string? error_message, uint? antrian_id = null)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KorektorBukuDbContext>();
            
            var log = new AdobeApiLog
            {
                AdobeCredentialsId = adobe_credentials_id,
                AntrianId = antrian_id,
                Activity = GetActivityFromEndpoint(endpoint, method),
                Endpoint = endpoint.Length > 255 ? endpoint[..255] : endpoint,
                Method = method,
                StatusCode = status_code,
                ResponseTimeMs = response_time_ms,
                ErrorMessage = error_message,
                CreatedAt = AppClock.Now
            };
            
            db.AdobeApiLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log API call for antrian_id: {AntrianId}", antrian_id);
        }
    }

    private static string? BuildAdobeTokenUrl(string? apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return null;

        return apiBaseUrl.TrimEnd('/') + "/token";
    }

    private static TimeSpan GetConfiguredPollingInterval(IConfiguration configuration)
    {
        return int.TryParse(configuration["Adobe:JobPollingIntervalMs"], out var milliseconds) && milliseconds >= 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : DefaultJobPollingInterval;
    }

    private static TimeSpan GetConfiguredPollingTimeout(IConfiguration configuration)
    {
        return int.TryParse(configuration["Adobe:JobPollingTimeoutSeconds"], out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultJobPollingTimeout;
    }

    private static string NormalizeJobStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim().Replace('_', ' ').ToLowerInvariant();
    }

    private static bool IsActiveJobStatus(string normalizedStatus)
    {
        return normalizedStatus is "in progress" or "not started" or "queued" or "pending" or "running";
    }

    private static bool IsTerminalFailureStatus(string normalizedStatus)
    {
        return normalizedStatus is "failed" or "error" or "cancelled" or "canceled" or "aborted";
    }

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static string BuildAdobeJobStatusErrorMessage(string? status, string? detail)
    {
        var statusLabel = string.IsNullOrWhiteSpace(status) ? "unknown" : status.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? $"Adobe job berakhir dengan status '{statusLabel}'."
            : $"Adobe job berakhir dengan status '{statusLabel}': {detail}";
    }

    private static string? ExtractAdobeErrorSummary(JsonElement result, string responseBody)
    {
        var details = new List<string>();
        AddErrorDetail(details, GetJsonString(result, "message"));
        AddErrorDetail(details, GetJsonString(result, "error"));

        if (TryGetObjectProperty(result, "error", out var errorObject))
        {
            AddErrorDetail(details, GetJsonString(errorObject, "message"));
            AddErrorDetail(details, GetJsonString(errorObject, "code"));
            AddErrorDetail(details, GetJsonString(errorObject, "type"));
        }

        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("errors", out var errorsElement) &&
            errorsElement.ValueKind == JsonValueKind.Array)
        {
            var count = 0;
            foreach (var item in errorsElement.EnumerateArray())
            {
                if (count++ >= 3)
                {
                    break;
                }

                if (item.ValueKind == JsonValueKind.String)
                {
                    AddErrorDetail(details, item.GetString());
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                AddErrorDetail(details, GetJsonString(item, "message"));
                AddErrorDetail(details, GetJsonString(item, "code"));
                AddErrorDetail(details, GetJsonString(item, "detail"));
            }
        }

        if (details.Count > 0)
        {
            return TruncateErrorText(string.Join("; ", details), 180);
        }

        return TruncateErrorText(responseBody, 180);
    }

    private static void AddErrorDetail(ICollection<string> details, string? value)
    {
        var normalized = TruncateErrorText(value, 180);
        if (string.IsNullOrWhiteSpace(normalized) ||
            details.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        details.Add(normalized);
    }

    private static string TruncateErrorText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 3)] + "...";
    }
    
    private string GetActivityFromEndpoint(string endpoint, string method)
    {
        if (endpoint.Contains("ims-na1.adobelogin.com") && method == "POST") return "get_token";
        if (endpoint.Contains("/assets") && method == "POST") return "create_upload_uri";
        if (endpoint.Contains("amazonaws.com") && method == "PUT") return "upload_document";
        if (endpoint.Contains("/operation/createpdf") && method == "POST") return "create_job";
        if (endpoint.Contains("operation") && method == "GET") return "get_status";
        if (endpoint.Contains("amazonaws.com") && method == "GET") return "download_pdf";
        return "unknown";
    }
}
