namespace TemporalShowcase.Contracts;

/// <summary>RabbitMQ topology shared between the publishing activity and the document processor consumer.</summary>
public static class RabbitMqTopology
{
    public const string ExchangeName = "document-processing";
    public const string QueueName = "document-processing.commands";
    public const string RoutingKey = "document.process";
}
