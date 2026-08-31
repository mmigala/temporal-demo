using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Exceptions;
using TemporalShowcase.Application.Http;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Tests.Http;

[TestFixture]
public class CapacityServiceClientTests
{
    private static CapacityServiceClient CreateClient(HttpResponseMessage response)
    {
        var handler = new StubHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://capacity-service.test") };
        var factory = new SingleClientFactory(httpClient);
        return new CapacityServiceClient(factory, NullLogger<CapacityServiceClient>.Instance);
    }

    [Test]
    public void ReserveAsync_ServiceReturns503_ThrowsRetryableException()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.That(
            () => client.ReserveAsync(new ReserveCapacityRequest("wf-1", "doc-1"), CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public void ReserveAsync_ServiceReturns400_ThrowsNonRetryableApplicationFailure()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.BadRequest));

        var ex = Assert.ThrowsAsync<ApplicationFailureException>(
            () => client.ReserveAsync(new ReserveCapacityRequest("wf-1", "doc-1"), CancellationToken.None));
        Assert.That(ex!.NonRetryable, Is.True);
    }

    [Test]
    public async Task ReserveAsync_ServiceReturns200_ReturnsDeserializedResponse()
    {
        var payload = new ReserveCapacityResponse("wf-1", AlreadyReserved: true);
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload),
        };
        var client = CreateClient(httpResponse);

        var result = await client.ReserveAsync(new ReserveCapacityRequest("wf-1", "doc-1"), CancellationToken.None);

        Assert.That(result.AlreadyReserved, Is.True);
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
