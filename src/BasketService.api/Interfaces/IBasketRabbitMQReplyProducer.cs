using BasketService.api.Messaging.Context;

namespace BasketService.api.Interfaces;

public interface IBasketRabbitMQReplyProducer
{
    Task PublishAsync(string payload, string messageType, BasketReplyContext replyContext, CancellationToken cancellationToken = default);
}
