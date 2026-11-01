using System.IdentityModel.Tokens.Jwt;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Envelopes;

namespace BasketService.api.Messaging.Deserialization.Resolver;

public sealed class BasketMessageDeserializerResolver : IBasketMessageDeserializerResolver
{
    private readonly IReadOnlyDictionary<string, Type> _messageTypes;
    private readonly IBasketMessageDeserializer _messageDeserializer;

    public BasketMessageDeserializerResolver( 
        IEnumerable<Type> messageTypes,
        IBasketMessageDeserializer messageDeserializer)
    {
        _messageTypes = messageTypes.ToDictionary(type => type.Name, StringComparer.Ordinal);
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

        if (!_messageTypes.TryGetValue(envelope.MessageType, out var messageType))
        {
            throw new InvalidOperationException($"Cannot deserialize {envelope.MessageType}");
        }

        return _messageDeserializer.Deserialize(envelope.Payload, messageType);
    }
}
