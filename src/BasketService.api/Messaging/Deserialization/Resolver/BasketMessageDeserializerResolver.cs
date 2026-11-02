using BasketService.api.Interfaces;
using BasketService.api.Messaging.Envelopes;
using BasketService.api.Messaging.Registry;

namespace BasketService.api.Messaging.Deserialization.Resolver;

public sealed class BasketMessageDeserializerResolver : IBasketMessageDeserializerResolver
{
    private readonly BasketMessageRegistry _messageRegistry;
    private readonly IBasketMessageDeserializer _messageDeserializer;

    public BasketMessageDeserializerResolver( 
        BasketMessageRegistry messageRegistry,
        IBasketMessageDeserializer messageDeserializer)
    {
        _messageRegistry = messageRegistry;
        _messageDeserializer = messageDeserializer;
    }

    public IBasketMessage Deserialize( BasketMessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrWhiteSpace(envelope.MessageType))
        {
            throw new InvalidOperationException(
                "The message type cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Payload))
        {
            throw new InvalidOperationException(
                "The message payload cannot be empty.");
        }

        var messageType = _messageRegistry.GetMessageType(envelope.MessageType);

        return _messageDeserializer.Deserialize(envelope.Payload, messageType);
    }
}
