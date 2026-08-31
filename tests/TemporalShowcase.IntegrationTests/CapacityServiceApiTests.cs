using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.IntegrationTests;

[TestFixture]
public class CapacityServiceApiTests
{
    private WebApplicationFactory<global::Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WebApplicationFactory<global::Program>();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task ReserveCapacity_FirstRequest_CreatesReservation()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/internal/capacity/reservations", new ReserveCapacityRequest("wf-1", "doc-1"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<ReserveCapacityResponse>();
        Assert.That(body!.AlreadyReserved, Is.False);
    }

    [Test]
    public async Task ReserveCapacity_RepeatedRequestForSameWorkflow_ReturnsExistingReservation()
    {
        await _client.PostAsJsonAsync("/api/internal/capacity/reservations", new ReserveCapacityRequest("wf-2", "doc-2"));

        var response = await _client.PostAsJsonAsync(
            "/api/internal/capacity/reservations", new ReserveCapacityRequest("wf-2", "doc-2"));

        var body = await response.Content.ReadFromJsonAsync<ReserveCapacityResponse>();
        Assert.That(body!.AlreadyReserved, Is.True);
    }

    [Test]
    public async Task ReserveCapacity_SimulateFailuresConfigured_ReturnsServiceUnavailableUntilThresholdReached()
    {
        var request = new ReserveCapacityRequest("wf-3", "doc-3", SimulateFailures: 2);

        var first = await _client.PostAsJsonAsync("/api/internal/capacity/reservations", request);
        var second = await _client.PostAsJsonAsync("/api/internal/capacity/reservations", request);
        var third = await _client.PostAsJsonAsync("/api/internal/capacity/reservations", request);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(third.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ReleaseCapacity_ExistingReservation_RemovesItAndReportsNotAlreadyReleased()
    {
        await _client.PostAsJsonAsync("/api/internal/capacity/reservations", new ReserveCapacityRequest("wf-4", "doc-4"));

        var response = await _client.DeleteAsync("/api/internal/capacity/reservations/wf-4");

        var body = await response.Content.ReadFromJsonAsync<ReleaseCapacityResponse>();
        Assert.That(body!.AlreadyReleased, Is.False);
    }

    [Test]
    public async Task ReleaseCapacity_RepeatedRequest_IsIdempotentAndReportsAlreadyReleased()
    {
        await _client.PostAsJsonAsync("/api/internal/capacity/reservations", new ReserveCapacityRequest("wf-5", "doc-5"));
        await _client.DeleteAsync("/api/internal/capacity/reservations/wf-5");

        var response = await _client.DeleteAsync("/api/internal/capacity/reservations/wf-5");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<ReleaseCapacityResponse>();
        Assert.That(body!.AlreadyReleased, Is.True);
    }
}
