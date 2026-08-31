using Temporalio.Activities;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Tests.Workflows;

/// <summary>
/// Hand-written test double for <see cref="TemporalShowcase.Application.Activities.DocumentProcessingActivities"/>.
/// Temporal resolves activities by their registered name, not by C# type, so a workflow's typed
/// activity call can be satisfied by any class exposing methods with matching activity names -
/// this lets tests substitute network-free fakes for the real HTTP/RabbitMQ-backed activities.
/// </summary>
public sealed class FakeDocumentProcessingActivities
{
    public int ReserveCapacityCallCount { get; private set; }

    public int ReleaseCapacityCallCount { get; private set; }

    public int PublishCallCount { get; private set; }

    public int ReconciliationCallCount { get; private set; }

    public Func<ReserveCapacityRequest, Task>? ReserveCapacityHandler { get; set; }

    public Func<string, Task>? ReleaseCapacityHandler { get; set; }

    public Func<ProcessDocumentCommand, Task>? PublishHandler { get; set; }

    public Func<string, Task<ProcessingStatusResponse>>? ReconciliationHandler { get; set; }

    [Activity]
    public async Task ReserveCapacityAsync(ReserveCapacityRequest request)
    {
        ReserveCapacityCallCount++;
        if (ReserveCapacityHandler is not null)
        {
            await ReserveCapacityHandler(request);
        }
    }

    [Activity]
    public async Task ReleaseCapacityAsync(string workflowId)
    {
        ReleaseCapacityCallCount++;
        if (ReleaseCapacityHandler is not null)
        {
            await ReleaseCapacityHandler(workflowId);
        }
    }

    [Activity]
    public async Task PublishProcessDocumentCommandAsync(ProcessDocumentCommand command)
    {
        PublishCallCount++;
        if (PublishHandler is not null)
        {
            await PublishHandler(command);
        }
    }

    [Activity]
    public async Task<ProcessingStatusResponse> GetDocumentProcessingStatusAsync(string documentId)
    {
        ReconciliationCallCount++;
        return ReconciliationHandler is not null
            ? await ReconciliationHandler(documentId)
            : new ProcessingStatusResponse(documentId, DocumentProcessingStatus.Pending, null);
    }
}
