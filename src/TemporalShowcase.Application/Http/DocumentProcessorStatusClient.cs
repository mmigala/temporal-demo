using System.Net;
using System.Net.Http.Json;
using TemporalShowcase.Application.Configuration;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Http;

/// <summary>Thin HTTP client used by the reconciliation activity to query the document processor.</summary>
public sealed class DocumentProcessorStatusClient(IHttpClientFactory httpClientFactory)
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(HttpClientNames.DocumentProcessor);

    public async Task<ProcessingStatusResponse> GetStatusAsync(string documentId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/api/internal/documents/{Uri.EscapeDataString(documentId)}/status", cancellationToken);

        // Not Found is treated as "still pending" for this demo: the processor has not yet
        // recorded any outcome for the document.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ProcessingStatusResponse(documentId, DocumentProcessingStatus.Pending, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Document processor status query failed with status {(int)response.StatusCode}: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<ProcessingStatusResponse>(cancellationToken))!;
    }
}
