using System.Text.Json;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Deserialization;

public sealed class DeleteBasketMessageDeserializer : IBasketMessageDeserialzier<DeleteBasketMessage>
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;


    public DeleteBasketMessageDeserializer()
    {
        _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string MessageType => nameof(DeleteBasketMessage);

    public DeleteBasketMessage Deserialize(string payload)
    {
        return JsonSerializer.Deserialize<DeleteBasketMessage>(payload, _jsonSerializerOptions)
        ??
        throw new JsonException("Failed to deserialize DeleteBasketMessage.");
    }
}
