namespace TemporalShowcase.Contracts;

/// <summary>Request body for <c>POST /api/internal/capacity/reservations</c>.</summary>
public sealed record ReserveCapacityRequest(string WorkflowId, string DocumentId, int SimulateFailures = 0);

/// <summary>Response for a capacity reservation. <see cref="AlreadyReserved"/> makes the reservation idempotent and observable in demos.</summary>
public sealed record ReserveCapacityResponse(string WorkflowId, bool AlreadyReserved);

/// <summary>Response for a capacity release. <see cref="AlreadyReleased"/> makes the release idempotent and observable in demos.</summary>
public sealed record ReleaseCapacityResponse(string WorkflowId, bool AlreadyReleased);
