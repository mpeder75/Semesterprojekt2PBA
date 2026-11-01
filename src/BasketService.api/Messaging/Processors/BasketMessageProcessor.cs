using BasketService.api.Interfaces;
using BasketService.api.Messaging.Context;
using BasketService.api.Messaging.Envelopes;

namespace BasketService.api.Messaging.Processors;

public sealed class BasketMessageProcessor : IBasketMessageProcessor
{
    private readonly IBasketMessageDeserializerResolver _messageDeserializerResolver;
    private readonly IBasketMessageDispatcher _messageDispatcher;
    private readonly IBasketMessageSerializer _messageSerializer;
    private readonly IBasketRabbitMQReplyProducer _replyProducer;

    public BasketMessageProcessor(
        IBasketMessageDeserializerResolver basketMessageDeserializerResolver,
        IBasketMessageDispatcher basketMessageDispatcher,
        IBasketMessageSerializer basketMessageSerializer,
        IBasketRabbitMQReplyProducer basketRabbitMQReplyProducer)
    {
        _messageDeserializerResolver = basketMessageDeserializerResolver;
        _messageDispatcher = basketMessageDispatcher;
        _messageSerializer = basketMessageSerializer;
        _replyProducer = basketRabbitMQReplyProducer;
    }

    //For general messages
    public async Task ProcesMessageAsync(BasketMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var message = _messageDeserializerResolver.Deserialize(envelope);

        await _messageDispatcher.DispatchMessageAsync(message, cancellationToken);
    }

    //For messages that requries a reply
    public async Task ProcessRequestAsync(BasketMessageEnvelope envelope, BasketReplyContext replyContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(replyContext);
        
        var message = _messageDeserializerResolver.Deserialize(envelope);

        var reply = await _messageDispatcher.DispatchRequestAsync(message, cancellationToken) 
        ?? 
        throw new InvalidOperationException($"Message handler for {message.GetType().Name} returned no reply.");

        var payload = _messageSerializer.Serialize(reply);

        await _replyProducer.PublishAsync(
            payload,
            reply.GetType().Name,
            replyContext,
            cancellationToken);
    }
}
