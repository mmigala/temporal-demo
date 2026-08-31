using Temporalio.Exceptions;
using Temporalio.Workflows;
using TemporalShowcase.Application.Activities;
using TemporalShowcase.Application.Configuration;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Workflows;

// Temporal workflows must be deterministic: given the same history, replaying this code must
// always produce the same sequence of commands (activity calls, timers, etc). This is how
// Temporal recovers a workflow after a worker crash - it does not persist local variables, it
// replays the workflow code from the start against the recorded event history and fast-forwards
// through already-completed steps. That means this class must never call DateTime.UtcNow,
// Guid.NewGuid, Task.Delay, a random number generator, or anything with I/O directly. Instead it
// uses the Workflow.* deterministic equivalents and delegates every real side effect (HTTP calls,
// RabbitMQ publish) to an activity, which Temporal is allowed to retry/re-execute safely.

/// <summary>
/// Orchestrates the document-processing business scenario: reserve capacity, publish a command
/// for asynchronous processing, wait for completion (falling back to HTTP reconciliation), and
/// compensate by releasing capacity if processing permanently fails.
/// </summary>
[Workflow]
public class DocumentProcessingWorkflow
{
    private string _documentId = string.Empty;
    private string _fileName = string.Empty;
    private DocumentProcessingStatus _status = DocumentProcessingStatus.Pending;
    private string _currentStep = "Pending";
    private int _processingAttemptCount;
    private bool _reconciliationUsed;
    private bool _capacityReserved;
    private string? _lastError;
    private DocumentProcessedSignal? _completionSignal;
    private DocumentProcessingResult? _result;

    [WorkflowRun]
    public async Task<DocumentProcessingResult> RunAsync(DocumentProcessingInput input)
    {
        _documentId = input.DocumentId;
        _fileName = input.FileName;
        var startedAt = Workflow.UtcNow;

        try
        {
            await ReserveCapacityAsync(input);
            await PublishProcessingCommandAsync(input);

            _status = DocumentProcessingStatus.WaitingForCompletion;
            _currentStep = "Waiting for processor completion signal";
            var succeeded = await WaitForCompletionOrReconcileAsync(input);

            if (!succeeded)
            {
                await ReleaseCapacityAsync();
                _status = DocumentProcessingStatus.Failed;
                _currentStep = "Failed";
                _result = BuildResult(DocumentProcessingStatus.Failed, startedAt);
                throw new ApplicationFailureException(_lastError ?? "Document processing failed permanently.");
            }

            _status = DocumentProcessingStatus.Completed;
            _currentStep = "Completed";
            _result = BuildResult(DocumentProcessingStatus.Completed, startedAt);
            return _result;
        }
        catch (Exception ex) when (ex is not ApplicationFailureException)
        {
            // Covers cancellation and unexpected activity failures. Capacity is only released
            // if it was actually reserved, so a cancellation before reservation is a no-op.
            if (_capacityReserved)
            {
                await ReleaseCapacityAsync();
            }

            throw;
        }
    }

    [WorkflowSignal]
    public async Task DocumentProcessedAsync(DocumentProcessedSignal signal)
    {
        if (signal.DocumentId != _documentId)
        {
            // Not for us - ignore. In a real system, misrouted signals should never happen, but
            // workflow code must be defensive since signals can arrive from any external caller.
            return;
        }

        // Ignore duplicate signals so an at-least-once delivery never overwrites a result that
        // has already been evaluated.
        _completionSignal ??= signal;
    }

    [WorkflowQuery]
    public DocumentProcessingState GetState() => new(
        _documentId,
        _fileName,
        _status,
        _currentStep,
        _processingAttemptCount,
        _reconciliationUsed,
        _lastError,
        _result);

    private async Task ReserveCapacityAsync(DocumentProcessingInput input)
    {
        _status = DocumentProcessingStatus.ReservingCapacity;
        _currentStep = "Reserving processing capacity";

        var request = new ReserveCapacityRequest(Workflow.Info.WorkflowId, input.DocumentId, input.SimulationOptions.SimulateCapacityFailures);
        await Workflow.ExecuteActivityAsync(
            (DocumentProcessingActivities act) => act.ReserveCapacityAsync(request),
            ActivityRetryPolicies.ReserveCapacity);

        _capacityReserved = true;
    }

    private async Task PublishProcessingCommandAsync(DocumentProcessingInput input)
    {
        _status = DocumentProcessingStatus.ProcessingRequested;
        _currentStep = "Publishing processing command";

        var command = new ProcessDocumentCommand(
            MessageId: Workflow.NewGuid().ToString(),
            WorkflowId: Workflow.Info.WorkflowId,
            DocumentId: input.DocumentId,
            FileName: input.FileName,
            SimulateProcessingFailure: input.SimulationOptions.SimulateProcessingFailure,
            SimulateLostCompletionSignal: input.SimulationOptions.SimulateLostCompletionSignal);

        await Workflow.ExecuteActivityAsync(
            (DocumentProcessingActivities act) => act.PublishProcessDocumentCommandAsync(command),
            ActivityRetryPolicies.PublishCommand);
    }

    private async Task ReleaseCapacityAsync()
    {
        _status = DocumentProcessingStatus.Compensating;
        _currentStep = "Releasing reserved capacity";

        await Workflow.ExecuteActivityAsync(
            (DocumentProcessingActivities act) => act.ReleaseCapacityAsync(Workflow.Info.WorkflowId),
            ActivityRetryPolicies.ReleaseCapacity);
    }

    private async Task<bool> WaitForCompletionOrReconcileAsync(DocumentProcessingInput input)
    {
        var receivedBeforeTimeout = await Workflow.WaitConditionAsync(
            () => _completionSignal != null, TemporalConstants.CompletionSignalTimeout);
        if (receivedBeforeTimeout)
        {
            return EvaluateCompletionSignal();
        }

        _status = DocumentProcessingStatus.Reconciling;
        _reconciliationUsed = true;

        for (var attempt = 1; attempt <= TemporalConstants.MaxReconciliationAttempts; attempt++)
        {
            _currentStep = $"Reconciling through HTTP (attempt {attempt} of {TemporalConstants.MaxReconciliationAttempts})";

            var statusResponse = await Workflow.ExecuteActivityAsync(
                (DocumentProcessingActivities act) => act.GetDocumentProcessingStatusAsync(input.DocumentId),
                ActivityRetryPolicies.Reconciliation);

            switch (statusResponse.Status)
            {
                case DocumentProcessingStatus.Completed:
                    _processingAttemptCount++;
                    return true;
                case DocumentProcessingStatus.Failed:
                    _processingAttemptCount++;
                    _lastError = statusResponse.Error;
                    return false;
                default:
                    // Still pending. Give the completion signal one more chance to arrive
                    // (it may have been in flight) before reconciling again through a durable timer.
                    if (attempt < TemporalConstants.MaxReconciliationAttempts)
                    {
                        var arrivedWhileWaiting = await Workflow.WaitConditionAsync(
                            () => _completionSignal != null, TemporalConstants.ReconciliationRetryDelay);
                        if (arrivedWhileWaiting)
                        {
                            return EvaluateCompletionSignal();
                        }
                    }

                    break;
            }
        }

        _lastError = "Reconciliation attempts were exhausted without a definitive processing outcome.";
        return false;
    }

    private bool EvaluateCompletionSignal()
    {
        _processingAttemptCount++;
        if (_completionSignal!.Succeeded)
        {
            return true;
        }

        _lastError = _completionSignal.Error;
        return false;
    }

    private DocumentProcessingResult BuildResult(DocumentProcessingStatus status, DateTime startedAt) => new(
        _documentId,
        status,
        _processingAttemptCount,
        _reconciliationUsed,
        startedAt,
        Workflow.UtcNow);
}
