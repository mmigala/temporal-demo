using Temporalio.Common;
using Temporalio.Workflows;

namespace TemporalShowcase.Application.Configuration;

/// <summary>
/// Demo-sized retry/timeout policies for each activity. Real values would be tuned to the
/// latency and failure characteristics of the dependency being called.
/// </summary>
public static class ActivityRetryPolicies
{
    public static ActivityOptions ReserveCapacity => new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(10),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromSeconds(5),
            MaximumAttempts = TemporalConstants.MaxCapacityReserveAttempts,
        },
    };

    public static ActivityOptions ReleaseCapacity => new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(10),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromSeconds(10),
            MaximumAttempts = 10,
        },

        // Releasing capacity is a compensating action that must run even when the workflow
        // itself has been cancelled, so it deliberately does not inherit the (already
        // cancelled) workflow cancellation token.
        CancellationToken = CancellationToken.None,
    };

    public static ActivityOptions PublishCommand => new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(10),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromSeconds(5),
            MaximumAttempts = 5,
        },
    };

    public static ActivityOptions Reconciliation => new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(5),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromSeconds(5),
            MaximumAttempts = 3,
        },
    };
}
