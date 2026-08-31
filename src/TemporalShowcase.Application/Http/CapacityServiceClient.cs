using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Temporalio.Exceptions;
using TemporalShowcase.Application.Configuration;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Http;

/// <summary>Thin HTTP client for the capacity service. All side effects happen here, never in workflow code.</summary>
public sealed class CapacityServiceClient(IHttpClientFactory httpClientFactory, ILogger<CapacityServiceClient> logger)
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(HttpClientNames.CapacityService);

    public async Task<ReserveCapacityResponse> ReserveAsync(ReserveCapacityRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/internal/capacity/reservations")
        {
            Content = JsonContent.Create(request),
        };
        // Idempotency key based on the workflow ID: the capacity service treats repeated
        // reservation requests for the same workflow as a no-op, which is required because
        // Temporal activities are executed at-least-once.
        httpRequest.Headers.Add("Idempotency-Key", request.WorkflowId);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        await EnsureRetryableSuccessAsync(response, "reserve capacity", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ReserveCapacityResponse>(cancellationToken))!;
    }

    public async Task<ReleaseCapacityResponse> ReleaseAsync(string workflowId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync(
            $"/api/internal/capacity/reservations/{Uri.EscapeDataString(workflowId)}", cancellationToken);
        await EnsureRetryableSuccessAsync(response, "release capacity", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ReleaseCapacityResponse>(cancellationToken))!;
    }

    private async Task EnsureRetryableSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;

        // Server errors and request timeouts are transient: throwing a plain exception lets
        // Temporal's activity retry policy retry the call. A 4xx here represents a permanent
        // validation problem, so it is surfaced as non-retryable to avoid wasting retries.
        if (statusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            logger.LogWarning("Capacity service {Operation} returned transient status {StatusCode}", operation, statusCode);
            throw new HttpRequestException($"Capacity service {operation} failed with transient status {statusCode}: {body}");
        }

        logger.LogError("Capacity service {Operation} returned permanent status {StatusCode}", operation, statusCode);
        throw new ApplicationFailureException(
            $"Capacity service {operation} failed with permanent status {statusCode}: {body}",
            nonRetryable: true);
    }
}
