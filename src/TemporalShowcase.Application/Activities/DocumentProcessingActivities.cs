using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using TemporalShowcase.Application.Http;
using TemporalShowcase.Application.Messaging;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Activities;

/// <summary>
/// All Temporal activities for the document-processing workflow. Every method here performs a
/// real side effect (HTTP call or RabbitMQ publish); this is intentional - workflow code must
/// stay deterministic, so anything that touches the network lives in an activity instead.
/// </summary>
public sealed class DocumentProcessingActivities(
    CapacityServiceClient capacityServiceClient,
    ProcessDocumentPublisher publisher,
    DocumentProcessorStatusClient statusClient,
    ILogger<DocumentProcessingActivities> logger)
{
    [Activity]
    public async Task ReserveCapacityAsync(ReserveCapacityRequest request)
    {
        var attempt = ActivityExecutionContext.Current.Info.Attempt;
        logger.LogInformation(
            "Reserving capacity for workflow {WorkflowId}, document {DocumentId} (attempt {Attempt})",
            request.WorkflowId,
            request.DocumentId,
            attempt);

        var response = await capacityServiceClient.ReserveAsync(request, ActivityExecutionContext.Current.CancellationToken);

        logger.LogInformation(
            "Capacity reservation for workflow {WorkflowId} resolved (already reserved: {AlreadyReserved})",
            request.WorkflowId,
            response.AlreadyReserved);
    }

    [Activity]
    public async Task ReleaseCapacityAsync(string workflowId)
    {
        var attempt = ActivityExecutionContext.Current.Info.Attempt;
        logger.LogInformation("Releasing capacity for workflow {WorkflowId} (attempt {Attempt})", workflowId, attempt);

        var response = await capacityServiceClient.ReleaseAsync(workflowId, ActivityExecutionContext.Current.CancellationToken);

        logger.LogInformation(
            "Capacity release for workflow {WorkflowId} resolved (already released: {AlreadyReleased})",
            workflowId,
            response.AlreadyReleased);
    }

    [Activity]
    public async Task PublishProcessDocumentCommandAsync(ProcessDocumentCommand command)
    {
        logger.LogInformation(
            "Publishing ProcessDocument command {MessageId} for workflow {WorkflowId}, document {DocumentId}",
            command.MessageId,
            command.WorkflowId,
            command.DocumentId);

        await publisher.PublishAsync(command, ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<ProcessingStatusResponse> GetDocumentProcessingStatusAsync(string documentId)
    {
        logger.LogInformation("Reconciling processing status for document {DocumentId}", documentId);

        return await statusClient.GetStatusAsync(documentId, ActivityExecutionContext.Current.CancellationToken);
    }
}
