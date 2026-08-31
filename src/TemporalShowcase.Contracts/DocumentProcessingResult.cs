namespace TemporalShowcase.Contracts;

/// <summary>Final result returned by <c>DocumentProcessingWorkflow.RunAsync</c> on success.</summary>
public sealed record DocumentProcessingResult(
    string DocumentId,
    DocumentProcessingStatus Status,
    int ProcessingAttemptCount,
    bool ReconciliationUsed,
    DateTime StartedAt,
    DateTime CompletedAt);
