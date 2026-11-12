using CatalogService.api.Messaging.Context;

namespace CatalogService.api.Interfaces;

public interface ICatalogRabbitMQReplyProducer
{
    Task PublishAsync(string payload, string messageType, CatalogReplyContext replyContext, CancellationToken cancellationToken = default);
}
