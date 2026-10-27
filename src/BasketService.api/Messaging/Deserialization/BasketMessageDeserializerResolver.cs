using System.IdentityModel.Tokens.Jwt;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Envelopes;

namespace BasketService.api.Messaging.Deserialization;

public sealed class BasketMessageDeserializerResolver
{
    private readonly IReadOnlyDictionary<string, IBasketMessageDeserializer> _messageDeserializers;

    public BasketMessageDeserializerResolver( IEnumerable<IBasketMessageDeserializer> messageDeserializers)
    {
        _messageDeserializers = messageDeserializers.ToDictionary(deserializer => deserializer.MessageType, StringComparer.Ordinal);
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

        if (!_messageDeserializers.TryGetValue(envelope.MessageType, out var messageDeserializer))
        {
            throw new InvalidOperationException($"Cannot deserialize {envelope.MessageType}");
        }

        return messageDeserializer.Deserialize(envelope.Payload);
    }
}
