namespace _.Services;

public interface IAuthService
{
    Task<object?> Login(string username, string password);
    Task<object?> RefreshToken(string refreshToken);
}
