using System.Text.Json;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Deserialization;

public sealed class AddItemToBasketMessageDeserializer : IBasketMessageDeserialzier<AddItemToBasketMessage>
{
    private readonly JsonSerializerOptions _serializerOptions;

    public AddItemToBasketMessageDeserializer()
    {
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string MessageType => nameof(AddItemToBasketMessage);

    public AddItemToBasketMessage Deserialize(string payload)
    {
        return JsonSerializer.Deserialize<AddItemToBasketMessage>(payload, _serializerOptions) 
        ?? 
        throw new JsonException("Failled to deserialize AddItemToBasketMessage.");
    }
}
