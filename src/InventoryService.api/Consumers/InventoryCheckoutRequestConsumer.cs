using System.Text.Json;
using InventoryService.api.Messages;
using InventoryService.api.Producers;
using InventoryService.api.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryService.api.Consumers;

public sealed class InventoryCheckoutRequestConsumer(
    IConfiguration configuration,
    IServiceProvider services,
    ILogger<InventoryCheckoutRequestConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["RabbitMQ:Uri"]))
            return;

        var connection = services.GetRequiredService<IConnection>();
        var producer = services.GetRequiredService<InventoryCheckoutReplyProducer>();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(CheckoutQueues.DeadLetterExchange, ExchangeType.Direct,
            durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(CheckoutQueues.InventoryDead, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(CheckoutQueues.InventoryDead, CheckoutQueues.DeadLetterExchange,
            CheckoutQueues.Inventory, cancellationToken: stoppingToken);

        // Order also declares this queue. The broker policy adds dead-lettering to both declarations.
        await channel.QueueDeclareAsync(CheckoutQueues.Inventory, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            if (await ProcessAsync(delivery, producer, stoppingToken))
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            else
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false,
                    cancellationToken: stoppingToken);
        };

        await channel.BasicConsumeAsync(CheckoutQueues.Inventory, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task<bool> ProcessAsync(BasicDeliverEventArgs delivery, InventoryCheckoutReplyProducer producer,
        CancellationToken stoppingToken)
    {
        CheckoutMessageEnvelope? request = null;
        CheckoutMessageEnvelope? reply = null;

        for (var attempt = 0; ; attempt++)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                request ??= JsonSerializer.Deserialize<CheckoutMessageEnvelope>(
                    delivery.Body.Span, CheckoutMessageEnvelope.JsonOptions)
                    ?? throw new JsonException("Empty inventory request.");

                if (reply is null)
                {
                    await using var scope = services.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<InventoryCheckoutHandler>();
                    reply = await handler.HandleAsync(request, stoppingToken);
                }

                await producer.SendAsync(reply, stoppingToken);
                return true;
            }
            catch (Exception error) when (reply is null && error is (JsonException or ArgumentException))
            {
                logger.LogWarning(error, "Malformed inventory request {MessageId}", request?.MessageId
                    ?? (Guid.TryParse(delivery.BasicProperties.MessageId, out var id) ? id : null));
                return false;
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                if (attempt == RetryDelays.Length)
                {
                    logger.LogError(error, "Inventory request {MessageId} for checkout {CheckoutId} exhausted retries",
                        request?.MessageId, request?.CheckoutId);
                    return false;
                }

                logger.LogWarning(error, "Retrying inventory request {MessageId} for checkout {CheckoutId}",
                    request?.MessageId, request?.CheckoutId);
                await Task.Delay(RetryDelays[attempt], stoppingToken);
            }
        }
    }
}
