namespace TemporalShowcase.Contracts;

/// <summary>RabbitMQ connection settings, bound from configuration/environment variables.</summary>
public sealed record RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";
}
