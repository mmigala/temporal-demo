using System.Collections.Concurrent;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.DocumentProcessor.Services;

/// <summary>
/// In-memory, thread-safe store shared by the RabbitMQ consumer and the status HTTP endpoint.
/// It also tracks processed message IDs so duplicate RabbitMQ deliveries are not processed twice.
///
/// This is intentionally not durable: a production system needs a real datastore so that
/// deduplication and status survive a process restart. For this demo, restarting the processor
/// loses in-memory state, which is one of the reasons the workflow's HTTP reconciliation path
/// exists - it re-derives truth from whatever the processor currently knows.
/// </summary>
public sealed class ProcessingStatusStore
{
    private readonly ConcurrentDictionary<string, ProcessingStatusResponse> _statusesByDocumentId = new();
    private readonly ConcurrentDictionary<string, byte> _processedMessageIds = new();

    public bool TryGetStatus(string documentId, out ProcessingStatusResponse status) =>
        _statusesByDocumentId.TryGetValue(documentId, out status!);

    public void SetStatus(ProcessingStatusResponse status) => _statusesByDocumentId[status.DocumentId] = status;

    public bool TryMarkMessageProcessed(string messageId) => _processedMessageIds.TryAdd(messageId, 0);
}
