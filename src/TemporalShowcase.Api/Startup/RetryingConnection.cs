namespace TemporalShowcase.Api.Startup;

/// <summary>Connects to a dependency with exponential backoff, since Docker Compose does not guarantee startup ordering.</summary>
public static class RetryingConnection
{
    public static async Task<T> ConnectWithRetryAsync<T>(
        Func<Task<T>> connect, ILogger logger, string dependencyName, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (true)
        {
            try
            {
                var result = await connect();
                logger.LogInformation("Connected to {Dependency}", dependencyName);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to connect to {Dependency}, retrying in {Delay}", dependencyName, delay);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
