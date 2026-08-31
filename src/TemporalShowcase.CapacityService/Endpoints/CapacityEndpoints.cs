using TemporalShowcase.CapacityService.Services;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.CapacityService.Endpoints;

/// <summary>Maps the HTTP endpoints for reserving and releasing simulated processing capacity.</summary>
public static class CapacityEndpoints
{
    public static void MapCapacityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/capacity/reservations", Reserve)
            .WithName("ReserveCapacity")
            .WithSummary("Reserves processing capacity for a workflow, idempotently.")
            .WithTags("Capacity");

        app.MapDelete("/api/internal/capacity/reservations/{workflowId}", Release)
            .WithName("ReleaseCapacity")
            .WithSummary("Releases a previously reserved capacity reservation, idempotently.")
            .WithTags("Capacity");
    }

    private static IResult Reserve(ReserveCapacityRequest request, ReservationStore store, ILogger<Program> logger)
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
    }

    private static IResult Release(string workflowId, ReservationStore store, ILogger<Program> logger)
    {
        var removed = store.RemoveReservation(workflowId);
        logger.LogInformation(
            "Release requested for workflow {WorkflowId} (already released: {AlreadyReleased})", workflowId, !removed);
        return Results.Ok(new ReleaseCapacityResponse(workflowId, AlreadyReleased: !removed));
    }
}
