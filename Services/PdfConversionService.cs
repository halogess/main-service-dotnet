using System.Net.Http.Headers;
using ValidasiTugasAkhir.MainService.Models;
using Microsoft.EntityFrameworkCore;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IPdfConversionService
{
    Task<byte[]> ConvertDocxToPdf(string docxFilePath);
    Task<byte[]> ConvertDocxToPdfWithCredential(string docxFilePath, string clientId, string clientSecret, int? adobe_credentials_id = null, uint? antrian_id = null);
}

public class PdfConversionService : IPdfConversionService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PdfConversionService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private static readonly Dictionary<string, AdobeToken> _tokenCache = new();

    public PdfConversionService(IConfiguration configuration, HttpClient httpClient, ILogger<PdfConversionService> logger, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<byte[]> ConvertDocxToPdf(string docxFilePath)
    {
        var clientId = _configuration["Adobe:ClientId"] ?? throw new InvalidOperationException("Adobe:ClientId not configured");
        var clientSecret = _configuration["Adobe:ClientSecret"] ?? throw new InvalidOperationException("Adobe:ClientSecret not configured");
        return await ConvertDocxToPdfWithCredential(docxFilePath, clientId, clientSecret);
    }

    public async Task<byte[]> ConvertDocxToPdfWithCredential(string docxFilePath, string clientId, string clientSecret, int? adobe_credentials_id = null, uint? antrian_id = null)
    {
        _logger.LogInformation("Memulai konversi DOCX ke PDF: {FilePath}, Antrian ID: {AntrianId}", docxFilePath, antrian_id);
        
        var tokenUrl = _configuration["Adobe:TokenUrl"];
        var apiBaseUrl = _configuration["Adobe:ApiBaseUrl"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tokenUrl) || string.IsNullOrEmpty(apiBaseUrl))
        {
            throw new InvalidOperationException("Adobe credentials tidak ditemukan");
        }

        var cacheKey = $"{clientId}:{clientSecret}";
        if (!_tokenCache.ContainsKey(cacheKey) || _tokenCache[cacheKey].IsExpired)
        {
            _logger.LogInformation("Getting new access token from Adobe");
            var token = await GetAccessTokenWithExpiry(clientId, clientSecret, tokenUrl, antrian_id);
            _tokenCache[cacheKey] = token;
        }
        else
        {
            _logger.LogInformation("Using cached access token");
        }

        var accessToken = _tokenCache[cacheKey].AccessToken;
        
        var (assetId, uploadUri) = await CreateUploadUri(accessToken, apiBaseUrl, adobe_credentials_id, antrian_id);
        _logger.LogInformation("Upload dokumen ke Adobe: AssetID={AssetId}", assetId);
        await UploadDocument(uploadUri, docxFilePath, adobe_credentials_id, antrian_id);
        
        var jobId = await CreateConversionJob(accessToken, assetId, apiBaseUrl, adobe_credentials_id, antrian_id);
        _logger.LogInformation("Menunggu job selesai: {JobId}", jobId);
        var downloadUri = await PollJobStatus(accessToken, jobId, adobe_credentials_id, antrian_id);
        
        var pdfBytes = await DownloadPdf(downloadUri, accessToken, adobe_credentials_id, antrian_id);
        _logger.LogInformation("Konversi selesai: {Size} bytes", pdfBytes.Length);
        return pdfBytes;
    }

    private async Task<AdobeToken> GetAccessTokenWithExpiry(string clientId, string clientSecret, string tokenUrl, uint? antrian_id = null)
    {
        var startTime = DateTime.Now;
        var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret)
        });

        var response = await _httpClient.SendAsync(request);
        var responseTime = (int)(DateTime.Now - startTime).TotalMilliseconds;
        var responseBody = await response.Content.ReadAsStringAsync();
        
        await LogApiCall(null, tokenUrl, "POST", (int)response.StatusCode, responseTime, response.IsSuccessStatusCode ? null : responseBody, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>() ?? throw new InvalidOperationException("Failed to get access token");
        
        return new AdobeToken
        {
            AccessToken = result.access_token,
            ExpiresAt = DateTime.Now.AddSeconds(result.expires_in - 300)
        };
    }

    private class TokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public int expires_in { get; set; }
    }

    private async Task<(string assetId, string uploadUri)> CreateUploadUri(string accessToken, string apiBaseUrl, int? adobe_credentials_id, uint? antrian_id = null)
    {
        var clientId = _configuration["Adobe:ClientId"];
        var endpoint = $"{apiBaseUrl}/assets";
        var startTime = DateTime.Now;
        
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("x-api-key", clientId);
        request.Content = JsonContent.Create(new { mediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document" });

        var response = await _httpClient.SendAsync(request);
        var responseTime = (int)(DateTime.Now - startTime).TotalMilliseconds;
        var responseBody = await response.Content.ReadAsStringAsync();
        
        await LogApiCall(adobe_credentials_id, endpoint, "POST", (int)response.StatusCode, responseTime, response.IsSuccessStatusCode ? null : responseBody, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AssetResponse>() ?? throw new InvalidOperationException("Failed to get asset response");
        return (result.AssetID, result.UploadUri);
    }

    private class AssetResponse
    {
        public string AssetID { get; set; } = string.Empty;
        public string UploadUri { get; set; } = string.Empty;
    }

    private async Task UploadDocument(string uploadUri, string filePath, int? adobe_credentials_id, uint? antrian_id = null)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var startTime = DateTime.Now;
        
        var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        request.Content = new ByteArrayContent(fileBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var response = await _httpClient.SendAsync(request);
        var responseTime = (int)(DateTime.Now - startTime).TotalMilliseconds;
        
        await LogApiCall(adobe_credentials_id, "S3 Upload", "PUT", (int)response.StatusCode, responseTime, null, antrian_id);
        
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> CreateConversionJob(string accessToken, string assetId, string apiBaseUrl, int? adobe_credentials_id, uint? antrian_id = null)
    {
        var clientId = _configuration["Adobe:ClientId"];
        var endpoint = $"{apiBaseUrl}/operation/createpdf";
        var startTime = DateTime.Now;
        
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("x-api-key", clientId);
        request.Content = JsonContent.Create(new { assetID = assetId });

        var response = await _httpClient.SendAsync(request);
        var responseTime = (int)(DateTime.Now - startTime).TotalMilliseconds;
        var responseBody = await response.Content.ReadAsStringAsync();
        
        await LogApiCall(adobe_credentials_id, endpoint, "POST", (int)response.StatusCode, responseTime, response.IsSuccessStatusCode ? null : responseBody, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var location = response.Headers.Location?.ToString() ?? throw new InvalidOperationException("Job location not found");
        return location;
    }

    private async Task<string> PollJobStatus(string accessToken, string jobUri, int? adobe_credentials_id, uint? antrian_id = null)
    {
        while (true)
        {
            var clientId = _configuration["Adobe:ClientId"];
            var startTime = DateTime.Now;
            
            var request = new HttpRequestMessage(HttpMethod.Get, jobUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("x-api-key", clientId);

            var response = await _httpClient.SendAsync(request);
            var responseTime = (int)(DateTime.Now - startTime).TotalMilliseconds;
            var responseBody = await response.Content.ReadAsStringAsync();
            
            await LogApiCall(adobe_credentials_id, jobUri, "GET", (int)response.StatusCode, responseTime, response.IsSuccessStatusCode ? null : responseBody, antrian_id);
            
            response.EnsureSuccessStatusCode();
            
            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseBody);
            var status = result.GetProperty("status").GetString();
            
            if (status == "done")
            {
                var asset = result.GetProperty("asset");
                var downloadUri = asset.GetProperty("downloadUri").GetString() ?? throw new InvalidOperationException("Download URI not found");
                return downloadUri;
            }

            await Task.Delay(2000);
        }
    }

    private async Task<byte[]> DownloadPdf(string downloadUri, string accessToken, int? adobe_credentials_id, uint? antrian_id = null)
    {
        var startTime = DateTime.Now;
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);

        var response = await _httpClient.SendAsync(request);
        var responseTime = (int)(DateTime.Now - startTime).TotalMilliseconds;
        
        await LogApiCall(adobe_credentials_id, "S3 Download", "GET", (int)response.StatusCode, responseTime, null, antrian_id);
        
        response.EnsureSuccessStatusCode();
        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
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
                ErrorMessage = error_message
            };
            
            db.AdobeApiLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log API call for antrian_id: {AntrianId}", antrian_id);
        }
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
