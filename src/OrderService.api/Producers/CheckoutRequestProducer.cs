using System.Text.Json;
using OrderService.api.Interfaces;
using OrderService.api.Messages;
using RabbitMQ.Client;

namespace OrderService.api.Producers;

public class CheckoutRequestProducer : ICheckoutRequestProducer
{
    // camelCase, som planen kræver (messageId, checkoutId, ...)
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnection _connection;

    public CheckoutRequestProducer(IConnection connection)
    {
        _connection = connection;
    }

    public async Task SendAsync(
        string queue,
        string messageType,
        Guid checkoutId,
        Guid correlationId,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(payload);

        // 1. Byg kuverten
        var envelope = new CheckoutMessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            CheckoutId = checkoutId,
            CorrelationId = correlationId,
            MessageType = messageType,
            SentAtUtc = DateTimeOffset.UtcNow,
            ReplyTo = CheckoutQueues.Replies,
            Payload = JsonSerializer.SerializeToElement(payload, payload.GetType(), JsonOptions)
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

        // 2. Åbn en kanal med publisher confirms: BasicPublishAsync venter på RabbitMQs bekræftelse
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var channel = await _connection.CreateChannelAsync(channelOptions, cancellationToken);

        // 3. Opret køen (samme indstillinger som servicens egen consumer), så beskeden bliver liggende,
        //    selv hvis servicen ikke er startet endnu
        await channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // 4. Egenskaber på selve beskeden (ReplyTo og CorrelationId bruger modtageren til at svare)
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Type = messageType,
            MessageId = envelope.MessageId.ToString(),
            CorrelationId = correlationId.ToString(),
            ReplyTo = CheckoutQueues.Replies,
            DeliveryMode = DeliveryModes.Persistent
        };

        // 5. Send via standard-exchange med kønavnet som routing key.
        //    mandatory: true giver fejl, hvis beskeden ikke kan leveres til en kø.
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
