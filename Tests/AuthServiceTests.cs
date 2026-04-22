using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ValidasiTugasAkhir.MainService.Models;
using Xunit;
using _.Services;

namespace Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task Login_ShouldReturnNull_WhenExternalApiResponseFalse()
    {
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        sttsDb.Mahasiswas.Add(new Mahasiswa
        {
            MhsNrp = "05111740000111",
            MhsNama = "Mahasiswa Uji"
        });
        await sttsDb.SaveChangesAsync();

        var factory = CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"response\":false}")
        });

        var service = CreateAuthService(sttsDb, factory);

        var result = await service.Login("05111740000111", "password-valid-sekalipun");

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_ShouldReturnToken_WhenExternalApiResponseTrue_AndMahasiswaExists()
    {
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        sttsDb.Mahasiswas.Add(new Mahasiswa
        {
            MhsNrp = "05111740000111",
            MhsNama = "Mahasiswa Uji"
        });
        await sttsDb.SaveChangesAsync();

        var factory = CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"response\":true}")
        });

        var service = CreateAuthService(sttsDb, factory);

        var result = await service.Login("05111740000111", "password-valid");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Login_ShouldReturnToken_WhenExternalApiResponseUsesStringOne()
    {
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        sttsDb.Mahasiswas.Add(new Mahasiswa
        {
            MhsNrp = "05111740000111",
            MhsNama = "Mahasiswa Uji"
        });
        await sttsDb.SaveChangesAsync();

        var factory = CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"response\":\"1\"}")
        });

        var service = CreateAuthService(sttsDb, factory);

        var result = await service.Login("05111740000111", "password-valid");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Login_ShouldEncodeExternalAuthUrl_AndTrimUsername()
    {
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        sttsDb.Mahasiswas.Add(new Mahasiswa
        {
            MhsNrp = "05111740000111",
            MhsNama = "Mahasiswa Uji"
        });
        await sttsDb.SaveChangesAsync();

        Uri? capturedUri = null;
        var factory = CreateHttpClientFactory(request =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":true}")
            };
        });

        var service = CreateAuthService(sttsDb, factory);

        await service.Login(" 05111740000111 ", "pa ss?+#");

        Assert.NotNull(capturedUri);
        Assert.Equal(
            "https://ws.stts.edu/credential/05111740000111/login/pa%20ss%3F%2B%23&appname=ta_korektor_buku",
            capturedUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Login_ShouldBypassExternalApi_WhenEnvironmentIsDevelopment()
    {
        await using var sttsDb = ControllerTestHelpers.CreateSttsDbContext();
        sttsDb.Mahasiswas.Add(new Mahasiswa
        {
            MhsNrp = "05111740000111",
            MhsNama = "Mahasiswa Uji"
        });
        await sttsDb.SaveChangesAsync();

        var requestCount = 0;
        var factory = CreateHttpClientFactory(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":false}")
            };
        });

        var service = CreateAuthService(sttsDb, factory, environmentName: Environments.Development);

        var result = await service.Login("05111740000111", "password-apa-saja");

        Assert.NotNull(result);
        Assert.Equal(0, requestCount);
    }

    private static AuthService CreateAuthService(
        SttsDbContext sttsDb,
        IHttpClientFactory httpClientFactory,
        Dictionary<string, string?>? overrides = null,
        string environmentName = "Production")
    {
        var values = new Dictionary<string, string?>
            {
                ["Auth:ExternalApiUrl"] = "https://ws.stts.edu/credential",
                ["Auth:ExternalApiToken"] = "dummy-token",
                ["Auth:AppName"] = "ta_korektor_buku",
                ["Auth:AdminUsername"] = "admin",
                ["Auth:JwtSecret"] = "12345678901234567890123456789012",
                ["Auth:AccessTokenExpiryMinutes"] = "15",
                ["Auth:RefreshTokenExpiryDays"] = "7"
            };

        if (overrides != null)
        {
            foreach (var item in overrides)
                values[item.Key] = item.Value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(environmentName);

        return new AuthService(
            sttsDb,
            configuration,
            httpClientFactory,
            NullLogger<AuthService>.Instance,
            environment.Object);
    }

    private static IHttpClientFactory CreateHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var handler = new StubHttpMessageHandler(responseFactory);
        var client = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(client);

        return mockFactory.Object;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
