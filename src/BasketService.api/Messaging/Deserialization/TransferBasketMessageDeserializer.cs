using System.Text.Json;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Deserialization;

public class TransferBasketMessageDeserializer : IBasketMessageDeserialzier<TransferBasketMessage>
{
    private readonly JsonSerializerOptions _serializerOptions;

    public TransferBasketMessageDeserializer()
    {
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string MessageType => nameof(TransferBasketMessage);

    public TransferBasketMessage Deserialize(string payload)
    {
        return JsonSerializer.Deserialize<TransferBasketMessage>(payload, _serializerOptions)
        ??
        throw new JsonException("Failled to deserialize SetBasketQuantitiesMessage.");
    }
}
