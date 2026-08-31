namespace TemporalShowcase.Contracts;

/// <summary>Response returned by the document processor's internal reconciliation status endpoint.</summary>
public sealed record ProcessingStatusResponse(
    string DocumentId,
    DocumentProcessingStatus Status,
    string? Error);
