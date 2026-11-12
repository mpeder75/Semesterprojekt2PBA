using System.Text.Json;
using InventoryService.api.Messages;
using RabbitMQ.Client;

namespace InventoryService.api.Producers;

public sealed class InventoryCheckoutReplyProducer(IConnection connection)
{
    public async Task SendAsync(CheckoutMessageEnvelope reply, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(options, timeout.Token);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Type = reply.MessageType,
            MessageId = reply.MessageId.ToString(),
            CorrelationId = reply.CorrelationId.ToString(),
            DeliveryMode = DeliveryModes.Persistent
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: CheckoutQueues.Replies,
            mandatory: true,
            basicProperties: properties,
            body: JsonSerializer.SerializeToUtf8Bytes(reply, CheckoutMessageEnvelope.JsonOptions),
            cancellationToken: timeout.Token);
    }
}
