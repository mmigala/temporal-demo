namespace TemporalShowcase.Contracts;

/// <summary>RabbitMQ command published by the worker's activity and consumed by the document processor.</summary>
public sealed record ProcessDocumentCommand(
    string MessageId,
    string WorkflowId,
    string DocumentId,
    string FileName,
    bool SimulateProcessingFailure,
    bool SimulateLostCompletionSignal);
