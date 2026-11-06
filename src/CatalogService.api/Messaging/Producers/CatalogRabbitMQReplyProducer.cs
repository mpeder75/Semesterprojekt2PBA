using System.Text;
using CatalogService.api.Interfaces;
using CatalogService.api.Messaging.Context;
using RabbitMQ.Client;

namespace CatalogService.api.Messaging.Producers;

public class CatalogRabbitMQReplyProducer : ICatalogRabbitMQReplyProducer
{
    private readonly IConnection _connection;

    public CatalogRabbitMQReplyProducer(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync(string payload, string messageType, CatalogReplyContext replyContext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(replyContext);

        if (string.IsNullOrWhiteSpace(replyContext.ReplyTo))
        {
            throw new InvalidOperationException("Cannont publish reply: ReplyTo is missing.");
        }

        await using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Type = messageType,
            CorrelationId = replyContext.CorrelationId,
            DeliveryMode = DeliveryModes.Persistent
        };

        var body = Encoding.UTF8.GetBytes(payload);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: replyContext.ReplyTo,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
