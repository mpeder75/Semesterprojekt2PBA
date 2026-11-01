using System.Text.Json;
using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Deserialization;

public sealed class BasketMessageDeserializer : IBasketMessageDeserializer
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public BasketMessageDeserializer()
    {
        _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public IBasketMessage Deserialize(string payload, Type messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(messageType);

        if (!typeof(IBasketMessage).IsAssignableFrom(messageType)) 
        {
            throw new ArgumentException($"{messageType.Name} must implement IBasketMessage");

        }

        return (IBasketMessage?)JsonSerializer.Deserialize(
                payload,
                messageType,
                _jsonSerializerOptions)
                ?? throw new JsonException(
                    $"Failed to deserialize {messageType.Name}.");
    }
}
