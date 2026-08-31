namespace TemporalShowcase.Contracts;

/// <summary>Temporal signal payload sent by the document processor once processing finishes.</summary>
public sealed record DocumentProcessedSignal(
    string DocumentId,
    bool Succeeded,
    string? Error);
