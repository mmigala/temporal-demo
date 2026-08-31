namespace TemporalShowcase.Contracts;

/// <summary>Request body accepted by <c>POST /api/documents</c>.</summary>
public sealed record StartDocumentProcessingRequest(
    string FileName,
    int SimulateCapacityFailures = 0,
    bool SimulateProcessingFailure = false,
    bool SimulateLostCompletionSignal = false);
