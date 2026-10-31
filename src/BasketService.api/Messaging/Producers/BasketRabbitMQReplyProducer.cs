using System.Text;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Context;
using RabbitMQ.Client;

namespace BasketService.api.Messaging.Producers;

public class BasketRabbitMQReplyProducer : IBasketRabbitMQReplyProducer
{
    private readonly IConnection _connection;

    public BasketRabbitMQReplyProducer(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync(string payload, string messageType, BasketReplyContext replyContext, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(replyContext);

        if (string.IsNullOrWhiteSpace(replyContext.ReplyTo))
        {
            throw new InvalidOperationException("Cannont publish reply: ReplyTo is missing.");
        }

        await using var channel = await _connection.CreateChannelAsync(cancellationToken: token);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Type = messageType,
            CorrelationId = replyContext.CorrelationId,
            DeliveryMode = DeliveryModes.Persistent
        };

        var body = Encoding.UTF8.GetBytes(payload);

        //TODO:
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: replyContext.ReplyTo,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: token);
    }
}
