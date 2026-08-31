namespace TemporalShowcase.Contracts;

/// <summary>
/// Snapshot of the workflow's in-progress state, returned by the <c>GetState</c> Temporal query
/// and surfaced through the API's GET endpoint.
/// </summary>
public sealed record DocumentProcessingState(
    string DocumentId,
    string FileName,
    DocumentProcessingStatus Status,
    string CurrentStep,
    int ProcessingAttemptCount,
    bool ReconciliationUsed,
    string? LastError,
    DocumentProcessingResult? Result);
