using System.Collections.Concurrent;

namespace TemporalShowcase.CapacityService.Services;

/// <summary>
/// In-memory reservation store. Thread-safe and process-local: a real system would back this
/// with durable storage, but for this demo the goal is to show idempotent reserve/release
/// semantics, not to build production capacity management.
/// </summary>
public sealed class ReservationStore
{
    private readonly ConcurrentDictionary<string, string> _reservations = new();
    private readonly ConcurrentDictionary<string, int> _reservationAttempts = new();

    public bool TryGetReservation(string workflowId, out string documentId) =>
        _reservations.TryGetValue(workflowId, out documentId!);

    public int IncrementReservationAttempts(string workflowId) =>
        _reservationAttempts.AddOrUpdate(workflowId, 1, (_, count) => count + 1);

    public void AddReservation(string workflowId, string documentId) =>
        _reservations[workflowId] = documentId;

    public bool RemoveReservation(string workflowId) => _reservations.TryRemove(workflowId, out _);
}
