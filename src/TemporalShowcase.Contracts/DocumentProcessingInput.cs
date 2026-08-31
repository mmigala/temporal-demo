namespace TemporalShowcase.Contracts;

/// <summary>Input passed to <c>DocumentProcessingWorkflow.RunAsync</c>.</summary>
public sealed record DocumentProcessingInput(
    string DocumentId,
    string FileName,
    SimulationOptions SimulationOptions);
