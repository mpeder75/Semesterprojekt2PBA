using System.Text.Json;
using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Deserializers;

public class CatalogMessageDeserializer : ICatalogMessageDeserializer
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public CatalogMessageDeserializer()
    {
        _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public ICatalogMessage Deserialize(string payload, Type messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(messageType);

        if (!typeof(ICatalogMessage).IsAssignableFrom(messageType)) 
        {
            throw new ArgumentException($"{messageType.Name} must implement IBasketMessage");
        }

        return (ICatalogMessage?)JsonSerializer.Deserialize(
            payload,
            messageType,
            _jsonSerializerOptions)
        ?? throw new JsonException($"Failed to deserialize {messageType.Name}.");
    }
}
