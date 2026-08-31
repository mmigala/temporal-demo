namespace TemporalShowcase.Application.Configuration;

/// <summary>Task queue name and workflow ID conventions shared by the API, worker, and tests.</summary>
public static class TemporalConstants
{
    /// <summary>Task queue the worker listens on and the API starts workflows on.</summary>
    public const string TaskQueue = "document-processing";

    /// <summary>How long the workflow waits for the processor's completion signal before reconciling through HTTP.</summary>
    public static readonly TimeSpan CompletionSignalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Delay between reconciliation attempts while processing is still reported as pending.</summary>
    public static readonly TimeSpan ReconciliationRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Caps reconciliation attempts so the demo always reaches a terminal state.</summary>
    public const int MaxReconciliationAttempts = 3;

    /// <summary>
    /// Maximum attempts for the ReserveCapacity activity's retry policy (see <see cref="ActivityRetryPolicies.ReserveCapacity"/>).
    /// A caller requesting at least this many simulated failures would exhaust the retry policy
    /// before ever succeeding, so this is also used to validate the API's request input.
    /// </summary>
    public const int MaxCapacityReserveAttempts = 5;

    /// <summary>Workflow IDs are derived from the document ID so starting the same document twice is a conflict, not a new run.</summary>
    public static string BuildWorkflowId(string documentId) => $"document-processing-{documentId}";
}
