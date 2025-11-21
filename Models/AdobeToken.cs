namespace ValidasiTugasAkhir.MainService.Models;

public class AdobeToken
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
