using TemporalShowcase.CapacityService.Services;
using TemporalShowcase.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ReservationStore>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapPost("/api/internal/capacity/reservations", (ReserveCapacityRequest request, ReservationStore store, ILogger<Program> logger) =>
{
    if (store.TryGetReservation(request.WorkflowId, out _))
    {
        logger.LogInformation("Reservation for workflow {WorkflowId} already exists", request.WorkflowId);
        return Results.Ok(new ReserveCapacityResponse(request.WorkflowId, AlreadyReserved: true));
    }

    var attempt = store.IncrementReservationAttempts(request.WorkflowId);
    if (attempt <= request.SimulateFailures)
    {
        logger.LogWarning(
            "Simulating transient failure {Attempt}/{SimulateFailures} for workflow {WorkflowId}",
            attempt,
            request.SimulateFailures,
            request.WorkflowId);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    store.AddReservation(request.WorkflowId, request.DocumentId);
    logger.LogInformation("Reserved capacity for workflow {WorkflowId}, document {DocumentId}", request.WorkflowId, request.DocumentId);
    return Results.Ok(new ReserveCapacityResponse(request.WorkflowId, AlreadyReserved: false));
});

app.MapDelete("/api/internal/capacity/reservations/{workflowId}", (string workflowId, ReservationStore store, ILogger<Program> logger) =>
{
    var removed = store.RemoveReservation(workflowId);
    logger.LogInformation(
        "Release requested for workflow {WorkflowId} (already released: {AlreadyReleased})", workflowId, !removed);
    return Results.Ok(new ReleaseCapacityResponse(workflowId, AlreadyReleased: !removed));
});

app.Run();

/// <summary>Entry point marker so integration tests can bootstrap this API via WebApplicationFactory.</summary>
public partial class Program
{
}
