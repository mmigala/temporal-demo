using System.Text.Json.Serialization;

namespace TemporalShowcase.Contracts;

/// <summary>
/// Status of a document-processing workflow. Serialized as a string so the value is stable and
/// readable in the Temporal event history and HTTP responses.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentProcessingStatus
{
    Pending,
    ReservingCapacity,
    ProcessingRequested,
    WaitingForCompletion,
    Reconciling,
    Compensating,
    Completed,
    Failed,
    Cancelled,
}
