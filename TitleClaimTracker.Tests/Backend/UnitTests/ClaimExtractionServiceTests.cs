using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class ClaimExtractionServiceTests
{
    [Fact]
    public async Task PythonProvider_MapsStructuredCamelCaseResponse()
    {
        var handler = new StaticResponseHandler(
            HttpStatusCode.OK,
            """
            {"mode":"deterministic","claimantName":"Fictional Owner","address":"10 Fictional Way","city":"Sample City","state":"tx","issueDescription":"owner=Fictional Owner","summary":"Fictional intake","missingFields":[],"followUpQuestions":[]}
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8010/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient("PythonIntelligence")).Returns(client);
        var service = CreateService(factory.Object);

        var result = await service.ExtractAsync("owner=Fictional Owner", CancellationToken.None);

        Assert.Equal("python-deterministic", result.Mode);
        Assert.Equal("Fictional Owner", result.ClaimantName);
        Assert.Equal("10 Fictional Way", result.Address);
        Assert.Equal("TX", result.State);
        Assert.Empty(result.MissingFields);
    }

    [Fact]
    public async Task PythonProvider_FallsBackToLocalExtractionWhenUnavailable()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.ServiceUnavailable, "{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8010/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient("PythonIntelligence")).Returns(client);
        var service = CreateService(factory.Object);

        var result = await service.ExtractAsync(
            "owner=Fictional Owner; address=10 Fictional Way; city=Sample City; state=tx",
            CancellationToken.None);

        Assert.Equal("deterministic", result.Mode);
        Assert.Equal("Fictional Owner", result.ClaimantName);
        Assert.Equal("TX", result.State);
        Assert.Empty(result.MissingFields);
    }

    [Fact]
    public async Task PythonProvider_PropagatesCallerCancellation()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8010/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient("PythonIntelligence")).Returns(client);
        var service = CreateService(factory.Object);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExtractAsync("owner=Fictional Owner", cancellation.Token));
    }

    private static ClaimExtractionService CreateService(IHttpClientFactory httpClientFactory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiService:Provider"] = "Python",
                ["AiService:TimeoutSeconds"] = "2",
            })
            .Build();
        return new ClaimExtractionService(httpClientFactory, configuration, NullLogger<ClaimExtractionService>.Instance);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("api/v1/extract", request.RequestUri?.PathAndQuery.TrimStart('/'));
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }
}