namespace ValidasiTugasAkhir.MainService.Services;

internal static class GeminiConfigurationDefaults
{
    public const string Model = "gemma-4-31b-it";
    public const string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public const double Temperature = 0.1;
    public const double TopP = 0.8;
    public const int TopK = 20;
    public const int MaxOutputTokens = 6000;
    public const string? ResponseMimeType = null;
    public const bool EnableLlm = true;

    public const int BatchSize = 6;
    public const int MaxBatchRetries = 2;
    public const int BatchDelaySeconds = 5;
    public const int MaxParallelBatches = 10;

    public const int MaxRetries = 3;
    public const string RetryDelaySeconds = "15,30,60";
    public const int DefaultRateLimitDelaySeconds = 60;
    public const int TokensPerMinutePerKeyLimit = 12000;
    public const int TokenWindowSeconds = 60;
    public const int MaxParseAttempts = 3;
    public const int ParseRetryDelaySeconds = 2;
    public const int CacheHours = 168;
}
