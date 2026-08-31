namespace TemporalShowcase.Contracts;

/// <summary>Response returned by <c>POST /api/documents</c>.</summary>
public sealed record StartDocumentProcessingResponse(
    string WorkflowId,
    string DocumentId,
    string StatusUrl);
