using System.Net;
using System.Net.Http.Json;
using TemporalShowcase.Application.Http;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Tests.Http;

[TestFixture]
public class DocumentProcessorStatusClientTests
{
    [Test]
    public async Task GetStatusAsync_ServiceReturns404_ReturnsPendingStatus()
    {
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
        var handler = new StubHttpMessageHandler(httpResponse);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://document-processor.test") };
        var client = new DocumentProcessorStatusClient(new SingleClientFactory(httpClient));

        var result = await client.GetStatusAsync("doc-1", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DocumentProcessingStatus.Pending));
    }

    [Test]
    public async Task GetStatusAsync_ServiceReturns200_ReturnsDeserializedStatus()
    {
        var payload = new ProcessingStatusResponse("doc-1", DocumentProcessingStatus.Completed, null);
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        var handler = new StubHttpMessageHandler(httpResponse);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://document-processor.test") };
        var client = new DocumentProcessorStatusClient(new SingleClientFactory(httpClient));

        var result = await client.GetStatusAsync("doc-1", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DocumentProcessingStatus.Completed));
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
