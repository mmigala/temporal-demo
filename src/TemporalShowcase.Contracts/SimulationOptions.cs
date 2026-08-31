namespace TemporalShowcase.Contracts;

/// <summary>
/// Toggles that let a demo caller deterministically trigger a specific Temporal behavior
/// (retries, reconciliation, permanent failure) instead of relying on real-world flakiness.
/// </summary>
public sealed record SimulationOptions
{
    /// <summary>Number of times the capacity service should reject the reservation with HTTP 503 before succeeding.</summary>
    public int SimulateCapacityFailures { get; init; }

    /// <summary>When true, the document processor reports a permanent processing failure.</summary>
    public bool SimulateProcessingFailure { get; init; }

    /// <summary>When true, the document processor completes the document but never sends the Temporal signal, forcing reconciliation.</summary>
    public bool SimulateLostCompletionSignal { get; init; }
}
