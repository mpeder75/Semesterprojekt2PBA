using CatalogService.api.Interfaces;
using CatalogService.api.Messaging.Envelopes;
using CatalogService.api.Messaging.Registry;

namespace CatalogService.api.Messaging.Deserialization.Resolvers;

public class CatalogMessageDeserializerResolver : ICatalogMessageDeserializerResolver
{
    private readonly CatalogMessageRegistry _messageRegistry;
    private readonly ICatalogMessageDeserializer _messageDeserializer;

    public CatalogMessageDeserializerResolver(CatalogMessageRegistry messageRegistry, ICatalogMessageDeserializer messageDeserializer)
    {
        _messageRegistry = messageRegistry;
        _messageDeserializer = messageDeserializer;
    }

    public ICatalogMessage Deserialize(CatalogMessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrWhiteSpace(envelope.MessageType))
        {
            throw new InvalidOperationException("The message type cannont be empty.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Payload))
        {
            throw new InvalidOperationException("The message type cannont be empty.");
        }

        var messageType = _messageRegistry.GetMessageType(envelope.MessageType);

        return _messageDeserializer.Deserialize(envelope.Payload, messageType);
    }
}
