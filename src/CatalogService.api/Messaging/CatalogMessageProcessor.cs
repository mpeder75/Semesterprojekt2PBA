using CatalogService.api.Interfaces;
using CatalogService.api.Messaging.Context;
using CatalogService.api.Messaging.Envelopes;

namespace CatalogService.api.Messaging;

public class CatalogMessageProcessor : ICatalogMessageProcessor
{
    private readonly ICatalogMessageDeserializerResolver _messageDeserializerResolver;
    private readonly ICatalogMessageDispatcher _messageDispatcher;
    private readonly ICatalogMessageSerializer _messageSerializer;
    private readonly ICatalogRabbitMQReplyProducer _producer;

    public CatalogMessageProcessor(
        ICatalogMessageDeserializerResolver messageDeserializerResolver, 
        ICatalogMessageDispatcher messageDispatcher, 
        ICatalogMessageSerializer messageSerializer, 
        ICatalogRabbitMQReplyProducer producer)
    {
        _messageDeserializerResolver = messageDeserializerResolver;
        _messageDispatcher = messageDispatcher;
        _messageSerializer = messageSerializer;
        _producer = producer;
    }

    public async Task ProcessRequestAsync(CatalogMessageEnvelope envelope, CatalogReplyContext replyContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(replyContext);

        var message = _messageDeserializerResolver.Deserialize(envelope);

        var reply = await _messageDispatcher.DispatchRequestAsync(message, cancellationToken)
        ??
        throw new InvalidOperationException($"Message handler for {message.GetType().Name} returned no reply.");

        var payload = _messageSerializer.Serialize(reply);
        var replyType = reply.GetType().Name;

        // En checkout-request får en hel kuvert tilbage (med checkoutId og correlationId), de gamle kun payload
        var body = CatalogReplyEnvelope.IsCheckoutRequest(envelope)
            ? CatalogReplyEnvelope.Build(envelope, replyContext, replyType, payload)
            : payload;

        await _producer.PublishAsync(
            body,
            replyType,
            replyContext,
            cancellationToken);
    }
}
