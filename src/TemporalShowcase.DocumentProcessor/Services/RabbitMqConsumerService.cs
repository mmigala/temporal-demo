using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Temporalio.Client;
using TemporalShowcase.Contracts;
using TemporalShowcase.DocumentProcessor.Configuration;

namespace TemporalShowcase.DocumentProcessor.Services;

/// <summary>
/// Consumes <see cref="ProcessDocumentCommand"/> messages, simulates document processing, and
/// signals the originating Temporal workflow. Uses manual acknowledgement: a message is only
/// acked once the processing outcome has been recorded in <see cref="ProcessingStatusStore"/>,
/// so a crash between "received" and "stored" causes RabbitMQ to redeliver the message.
/// </summary>
public sealed class RabbitMqConsumerService(
    IConnection connection,
    ProcessingStatusStore store,
    ITemporalClient temporalClient,
    IOptions<ProcessingOptions> processingOptions,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(
            RabbitMqTopology.ExchangeName, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(
            RabbitMqTopology.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(
            RabbitMqTopology.QueueName, RabbitMqTopology.ExchangeName, RabbitMqTopology.RoutingKey, cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(RabbitMqTopology.QueueName, autoAck: false, consumer, cancellationToken: stoppingToken);

        logger.LogInformation("Listening for ProcessDocument commands on queue {Queue}", RabbitMqTopology.QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        ProcessDocumentCommand? command;
        try
        {
            command = JsonSerializer.Deserialize<ProcessDocumentCommand>(args.Body.Span);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Rejecting malformed ProcessDocument message (delivery tag {DeliveryTag})", args.DeliveryTag);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (command is null || string.IsNullOrWhiteSpace(command.DocumentId) || string.IsNullOrWhiteSpace(command.WorkflowId))
        {
            logger.LogWarning("Rejecting invalid ProcessDocument message (delivery tag {DeliveryTag})", args.DeliveryTag);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (!store.TryMarkMessageProcessed(command.MessageId))
        {
            logger.LogInformation("Ignoring duplicate ProcessDocument command {MessageId}", command.MessageId);
            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            await Task.Delay(processingOptions.Value.ProcessingDelay, CancellationToken.None);

            var succeeded = !command.SimulateProcessingFailure;
            var status = succeeded ? DocumentProcessingStatus.Completed : DocumentProcessingStatus.Failed;
            var error = succeeded ? null : "Simulated permanent processing failure.";
            store.SetStatus(new ProcessingStatusResponse(command.DocumentId, status, error));

            // Acknowledge only after the outcome is durable in the (in-memory) status store.
            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false);

            logger.LogInformation("Processed document {DocumentId} with outcome {Status}", command.DocumentId, status);

            if (command.SimulateLostCompletionSignal)
            {
                logger.LogWarning(
                    "Simulating a lost completion signal for document {DocumentId}; the workflow must reconcile", command.DocumentId);
                return;
            }

            await SignalWorkflowAsync(command, succeeded, error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transient failure processing delivery {DeliveryTag}; requeueing", args.DeliveryTag);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task SignalWorkflowAsync(ProcessDocumentCommand command, bool succeeded, string? error)
    {
        try
        {
            var handle = temporalClient.GetWorkflowHandle(command.WorkflowId);
            var signal = new DocumentProcessedSignal(command.DocumentId, succeeded, error);
            await handle.SignalAsync(TemporalSignalNames.DocumentProcessed, [signal]);
            logger.LogInformation("Signalled workflow {WorkflowId} for document {DocumentId}", command.WorkflowId, command.DocumentId);
        }
        catch (Exception ex)
        {
            // The signal is best-effort: if it cannot be delivered, the workflow's own
            // reconciliation timeout will discover the outcome through the status HTTP endpoint.
            logger.LogError(ex, "Failed to signal workflow {WorkflowId} for document {DocumentId}", command.WorkflowId, command.DocumentId);
        }
    }
}
