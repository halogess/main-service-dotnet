using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ValidasiTugasAkhir.MainService.Models;
using ValidasiTugasAkhir.MainService.Services;
using Xunit;

namespace Tests;

public class PdfConversionServiceTests
{
    [Fact]
    public async Task ConvertDocxToPdfWithCredential_ShouldThrowClearError_WhenAdobeJobFails()
    {
        using var fixture = await CreateFixtureAsync(
            new SequenceHttpMessageHandler((requestCount, request, _) => Task.FromResult(CreateFailureFlowResponse(requestCount))),
            new Dictionary<string, string?>
            {
                ["Adobe:ApiBaseUrl"] = "https://pdf-services-ue1.adobe.io",
                ["Adobe:JobPollingIntervalMs"] = "1",
                ["Adobe:JobPollingTimeoutSeconds"] = "5"
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ConvertDocxToPdfWithCredential(
            fixture.DocxPath,
            $"client-{Guid.NewGuid():N}",
            "secret",
            adobe_credentials_id: 7,
            antrian_id: 2310));

        Assert.Contains("status 'failed'", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unsupported content", ex.Message, StringComparison.OrdinalIgnoreCase);

    }

    [Fact]
    public async Task ConvertDocxToPdfWithCredential_ShouldTimeout_WhenAdobeJobNeverFinishes()
    {
        using var fixture = await CreateFixtureAsync(
            new SequenceHttpMessageHandler((requestCount, request, _) => Task.FromResult(CreateInProgressFlowResponse(requestCount))),
            new Dictionary<string, string?>
            {
                ["Adobe:ApiBaseUrl"] = "https://pdf-services-ue1.adobe.io",
                ["Adobe:JobPollingIntervalMs"] = "10",
                ["Adobe:JobPollingTimeoutSeconds"] = "1"
            });

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => fixture.Service.ConvertDocxToPdfWithCredential(
            fixture.DocxPath,
            $"client-{Guid.NewGuid():N}",
            "secret",
            adobe_credentials_id: 8,
            antrian_id: 2311));

        Assert.Contains("tidak selesai", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConvertDocxToPdfWithCredential_ShouldHonorCancellationToken_WhenPollingAdobeJob()
    {
        using var fixture = await CreateFixtureAsync(
            new SequenceHttpMessageHandler((requestCount, request, _) => Task.FromResult(CreateInProgressFlowResponse(requestCount))),
            new Dictionary<string, string?>
            {
                ["Adobe:ApiBaseUrl"] = "https://pdf-services-ue1.adobe.io",
                ["Adobe:JobPollingIntervalMs"] = "50",
                ["Adobe:JobPollingTimeoutSeconds"] = "30"
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ConvertDocxToPdfWithCredential(
            fixture.DocxPath,
            $"client-{Guid.NewGuid():N}",
            "secret",
            adobe_credentials_id: 9,
            antrian_id: 2312,
            cancellationToken: cts.Token));
    }

    private static async Task<PdfConversionFixture> CreateFixtureAsync(
        HttpMessageHandler handler,
        IDictionary<string, string?> configurationValues)
    {
        var services = new ServiceCollection();
        services.AddDbContext<KorektorBukuDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        var serviceProvider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        var httpClient = new HttpClient(handler);
        var service = new PdfConversionService(
            configuration,
            httpClient,
            NullLogger<PdfConversionService>.Instance,
            serviceProvider);

        var docxPath = Path.Combine(Path.GetTempPath(), $"pdf-conversion-{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(docxPath, Encoding.UTF8.GetBytes("fake-docx"));

        return new PdfConversionFixture(serviceProvider, httpClient, service, docxPath);
    }

    private static HttpResponseMessage CreateFailureFlowResponse(int requestCount)
    {
        return requestCount switch
        {
            1 => CreateJsonResponse(HttpStatusCode.OK, """
                {"access_token":"access-token","expires_in":3600}
                """),
            2 => CreateJsonResponse(HttpStatusCode.OK, """
                {"AssetID":"asset-1","UploadUri":"https://upload.example.test/asset-1"}
                """),
            3 => new HttpResponseMessage(HttpStatusCode.OK),
            4 => CreateJobCreatedResponse("https://pdf-services-ue1.adobe.io/operation/createpdf/job-1/status"),
            5 => CreateJsonResponse(HttpStatusCode.OK, """
                {"status":"failed","error":{"message":"Unsupported content"}}
                """),
            _ => throw new InvalidOperationException($"Unexpected request #{requestCount}.")
        };
    }

    private static HttpResponseMessage CreateInProgressFlowResponse(int requestCount)
    {
        return requestCount switch
        {
            1 => CreateJsonResponse(HttpStatusCode.OK, """
                {"access_token":"access-token","expires_in":3600}
                """),
            2 => CreateJsonResponse(HttpStatusCode.OK, """
                {"AssetID":"asset-1","UploadUri":"https://upload.example.test/asset-1"}
                """),
            3 => new HttpResponseMessage(HttpStatusCode.OK),
            4 => CreateJobCreatedResponse("https://pdf-services-ue1.adobe.io/operation/createpdf/job-2/status"),
            _ => CreateJsonResponse(HttpStatusCode.OK, """
                {"status":"in progress"}
                """)
        };
    }

    private static HttpResponseMessage CreateJobCreatedResponse(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Created);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SequenceHttpMessageHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private int _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var currentRequest = Interlocked.Increment(ref _requestCount);
            return responder(currentRequest, request, cancellationToken);
        }
    }

    private sealed class PdfConversionFixture(
        ServiceProvider serviceProvider,
        HttpClient httpClient,
        PdfConversionService service,
        string docxPath) : IDisposable
    {
        public ServiceProvider ServiceProvider { get; } = serviceProvider;
        public HttpClient HttpClient { get; } = httpClient;
        public PdfConversionService Service { get; } = service;
        public string DocxPath { get; } = docxPath;

        public void Dispose()
        {
            HttpClient.Dispose();
            ServiceProvider.Dispose();

            if (File.Exists(DocxPath))
            {
                File.Delete(DocxPath);
            }
        }
    }
}
