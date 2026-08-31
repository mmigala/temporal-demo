using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Application.Messaging;

/// <summary>
/// Publishes <see cref="ProcessDocumentCommand"/> messages with publisher confirmations enabled.
/// The exchange/queue/binding are (re)declared on every publish so the topology is guaranteed to
/// exist regardless of startup ordering between the worker and RabbitMQ.
/// </summary>
public sealed class ProcessDocumentPublisher(IConnection connection, ILogger<ProcessDocumentPublisher> logger)
{
    public async Task PublishAsync(ProcessDocumentCommand command, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await channel.ExchangeDeclareAsync(
            RabbitMqTopology.ExchangeName, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            RabbitMqTopology.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            RabbitMqTopology.QueueName, RabbitMqTopology.ExchangeName, RabbitMqTopology.RoutingKey, cancellationToken: cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(command);
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = command.MessageId,
            CorrelationId = command.WorkflowId,
            ContentType = "application/json",
        };

        // With publisher confirmations enabled, BasicPublishAsync completes only once the
        // broker has acknowledged the message; a nack or channel-level exception here
        // surfaces as an activity failure so Temporal retries the publish.
        await channel.BasicPublishAsync(
            RabbitMqTopology.ExchangeName,
            RabbitMqTopology.RoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Published ProcessDocument command {MessageId} for workflow {WorkflowId} to {Exchange}",
            command.MessageId,
            command.WorkflowId,
            RabbitMqTopology.ExchangeName);
    }
}
