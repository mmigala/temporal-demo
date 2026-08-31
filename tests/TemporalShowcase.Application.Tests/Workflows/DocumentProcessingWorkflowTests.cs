using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Temporalio.Worker;
using TemporalShowcase.Application.Workflows;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Tests.Workflows;

[TestFixture]
public class DocumentProcessingWorkflowTests
{
    private static DocumentProcessingInput CreateInput(SimulationOptions? simulationOptions = null) =>
        new("doc-1", "example.pdf", simulationOptions ?? new SimulationOptions());

    private static async Task<WorkflowTestRun> RunAsync(
        FakeDocumentProcessingActivities activities,
        DocumentProcessingInput input,
        Func<WorkflowHandle<DocumentProcessingWorkflow, DocumentProcessingResult>, Task>? duringExecution = null)
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"test-queue-{Guid.NewGuid()}")
                .AddWorkflow<DocumentProcessingWorkflow>()
                .AddActivity(activities.ReserveCapacityAsync)
                .AddActivity(activities.ReleaseCapacityAsync)
                .AddActivity(activities.PublishProcessDocumentCommandAsync)
                .AddActivity(activities.GetDocumentProcessingStatusAsync));

        DocumentProcessingResult? result = null;
        WorkflowFailedException? failure = null;
        DocumentProcessingState? finalState = null;

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (DocumentProcessingWorkflow wf) => wf.RunAsync(input),
                new(id: $"wf-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            if (duringExecution is not null)
            {
                await duringExecution(handle);
            }

            try
            {
                result = await handle.GetResultAsync();
            }
            catch (WorkflowFailedException ex)
            {
                failure = ex;
            }

            // Queries remain answerable after a workflow completes or fails, which is exactly
            // what a caller polling GetState via the API would observe.
            finalState = await handle.QueryAsync(wf => wf.GetState());
        });

        return new WorkflowTestRun(result, failure, finalState);
    }

    private static async Task WaitForStatusAsync(
        WorkflowHandle<DocumentProcessingWorkflow, DocumentProcessingResult> handle, DocumentProcessingStatus status)
    {
        for (var i = 0; i < 300; i++)
        {
            var state = await handle.QueryAsync(wf => wf.GetState());
            if (state.Status == status)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Fail($"Workflow never reached status {status}.");
    }

    [Test]
    public async Task RunAsync_AllOperationsSucceed_ReturnsCompletedResult()
    {
        var activities = new FakeDocumentProcessingActivities();
        var input = CreateInput();

        var run = await RunAsync(activities, input, async handle =>
        {
            await WaitForStatusAsync(handle, DocumentProcessingStatus.WaitingForCompletion);
            await handle.SignalAsync(wf => wf.DocumentProcessedAsync(new DocumentProcessedSignal(input.DocumentId, true, null)));
        });

        Assert.That(run.Failure, Is.Null);
        Assert.That(run.Result, Is.Not.Null);
        Assert.That(run.Result!.Status, Is.EqualTo(DocumentProcessingStatus.Completed));
        Assert.That(run.Result.ReconciliationUsed, Is.False);
        Assert.That(activities.ReleaseCapacityCallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_CapacityActivityFailsTransiently_RetriesAndCompletes()
    {
        var activities = new FakeDocumentProcessingActivities();
        var failuresRemaining = 3;
        activities.ReserveCapacityHandler = _ =>
        {
            if (failuresRemaining > 0)
            {
                failuresRemaining--;
                throw new InvalidOperationException("Simulated transient capacity failure.");
            }

            return Task.CompletedTask;
        };

        var input = CreateInput();
        var run = await RunAsync(activities, input, async handle =>
        {
            await WaitForStatusAsync(handle, DocumentProcessingStatus.WaitingForCompletion);
            await handle.SignalAsync(wf => wf.DocumentProcessedAsync(new DocumentProcessedSignal(input.DocumentId, true, null)));
        });

        Assert.That(run.Failure, Is.Null);
        Assert.That(run.Result!.Status, Is.EqualTo(DocumentProcessingStatus.Completed));
        Assert.That(activities.ReserveCapacityCallCount, Is.EqualTo(4));
    }

    [Test]
    public async Task RunAsync_CompletionSignalIsDuplicated_CompletesOnlyOnce()
    {
        var activities = new FakeDocumentProcessingActivities();
        var input = CreateInput();

        var run = await RunAsync(activities, input, async handle =>
        {
            await WaitForStatusAsync(handle, DocumentProcessingStatus.WaitingForCompletion);
            await handle.SignalAsync(wf => wf.DocumentProcessedAsync(new DocumentProcessedSignal(input.DocumentId, true, null)));
            await handle.SignalAsync(wf => wf.DocumentProcessedAsync(new DocumentProcessedSignal(input.DocumentId, false, "should be ignored")));
        });

        Assert.That(run.Failure, Is.Null);
        Assert.That(run.Result!.Status, Is.EqualTo(DocumentProcessingStatus.Completed));
        Assert.That(run.Result.ProcessingAttemptCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_SignalContainsDifferentDocumentId_IgnoresSignal()
    {
        var activities = new FakeDocumentProcessingActivities
        {
            ReconciliationHandler = documentId => Task.FromResult(new ProcessingStatusResponse(documentId, DocumentProcessingStatus.Completed, null)),
        };
        var input = CreateInput();

        var run = await RunAsync(activities, input, async handle =>
        {
            await WaitForStatusAsync(handle, DocumentProcessingStatus.WaitingForCompletion);
            await handle.SignalAsync(wf => wf.DocumentProcessedAsync(new DocumentProcessedSignal("some-other-doc", true, null)));
        });

        Assert.That(run.Failure, Is.Null);
        Assert.That(run.Result!.Status, Is.EqualTo(DocumentProcessingStatus.Completed));
        Assert.That(run.Result.ReconciliationUsed, Is.True);
        Assert.That(activities.ReconciliationCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_SignalIsNotReceived_UsesReconciliation()
    {
        var activities = new FakeDocumentProcessingActivities
        {
            ReconciliationHandler = documentId => Task.FromResult(new ProcessingStatusResponse(documentId, DocumentProcessingStatus.Completed, null)),
        };
        var input = CreateInput();

        var run = await RunAsync(activities, input);

        Assert.That(run.Failure, Is.Null);
        Assert.That(run.Result!.Status, Is.EqualTo(DocumentProcessingStatus.Completed));
        Assert.That(run.Result.ReconciliationUsed, Is.True);
        Assert.That(activities.ReconciliationCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_ReconciliationReportsPending_ReconcilesAgain()
    {
        var callCount = 0;
        var activities = new FakeDocumentProcessingActivities
        {
            ReconciliationHandler = documentId =>
            {
                callCount++;
                var status = callCount < 2 ? DocumentProcessingStatus.Pending : DocumentProcessingStatus.Completed;
                return Task.FromResult(new ProcessingStatusResponse(documentId, status, null));
            },
        };
        var input = CreateInput();

        var run = await RunAsync(activities, input);

        Assert.That(run.Failure, Is.Null);
        Assert.That(run.Result!.Status, Is.EqualTo(DocumentProcessingStatus.Completed));
        Assert.That(activities.ReconciliationCallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_ReconciliationNeverCompletes_ReleasesCapacityAndFails()
    {
        var activities = new FakeDocumentProcessingActivities
        {
            ReconciliationHandler = documentId => Task.FromResult(new ProcessingStatusResponse(documentId, DocumentProcessingStatus.Pending, null)),
        };
        var input = CreateInput();

        var run = await RunAsync(activities, input);

        Assert.That(run.Result, Is.Null);
        Assert.That(run.Failure, Is.Not.Null);
        Assert.That(activities.ReconciliationCallCount, Is.EqualTo(3));
        Assert.That(activities.ReleaseCapacityCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_ProcessingReportsPermanentFailure_ReleasesCapacityAndFails()
    {
        var activities = new FakeDocumentProcessingActivities();
        var input = CreateInput();

        var run = await RunAsync(activities, input, async handle =>
        {
            await WaitForStatusAsync(handle, DocumentProcessingStatus.WaitingForCompletion);
            await handle.SignalAsync(
                wf => wf.DocumentProcessedAsync(new DocumentProcessedSignal(input.DocumentId, false, "Simulated permanent processing failure.")));
        });

        Assert.That(run.Result, Is.Null);
        Assert.That(run.Failure, Is.Not.Null);
        Assert.That(activities.ReleaseCapacityCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_CancelledBeforeCapacityReservation_DoesNotReleaseCapacity()
    {
        var reserveStarted = new TaskCompletionSource();
        var activities = new FakeDocumentProcessingActivities
        {
            ReserveCapacityHandler = async _ =>
            {
                reserveStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30));
            },
        };
        var input = CreateInput();

        var run = await RunAsync(activities, input, async handle =>
        {
            await reserveStarted.Task;
            await handle.CancelAsync();
        });

        Assert.That(run.Result, Is.Null);
        Assert.That(run.Failure, Is.Not.Null);
        Assert.That(activities.ReleaseCapacityCallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_CancelledAfterCapacityReservation_ReleasesCapacity()
    {
        var activities = new FakeDocumentProcessingActivities();
        var input = CreateInput();

        var run = await RunAsync(activities, input, async handle =>
        {
            await WaitForStatusAsync(handle, DocumentProcessingStatus.WaitingForCompletion);
            await handle.CancelAsync();
        });

        Assert.That(run.Result, Is.Null);
        Assert.That(run.Failure, Is.Not.Null);
        Assert.That(activities.ReleaseCapacityCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_ReserveCapacityActivityExhaustsRetries_ReportsFailedState()
    {
        var activities = new FakeDocumentProcessingActivities
        {
            ReserveCapacityHandler = _ => throw new InvalidOperationException("Simulated permanent capacity outage."),
        };
        var input = CreateInput();

        var run = await RunAsync(activities, input);

        Assert.That(run.Result, Is.Null);
        Assert.That(run.Failure, Is.Not.Null);
        Assert.That(activities.ReleaseCapacityCallCount, Is.EqualTo(0));
        Assert.That(run.FinalState!.Status, Is.EqualTo(DocumentProcessingStatus.Failed));
        Assert.That(run.FinalState.LastError, Is.Not.Null);
    }

    private sealed record WorkflowTestRun(DocumentProcessingResult? Result, WorkflowFailedException? Failure, DocumentProcessingState? FinalState);
}
