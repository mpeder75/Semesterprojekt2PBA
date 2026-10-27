using System.Text.Json;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Deserialization;

public class SetBasketQuantitiesMessageDeserializer : IBasketMessageDeserialzier<SetBasketQuantitiesMessage>
{
    private readonly JsonSerializerOptions _serializerOptions;

    public SetBasketQuantitiesMessageDeserializer()
    {
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string MessageType => nameof(SetBasketQuantitiesMessage);

    public SetBasketQuantitiesMessage Deserialize(string payload)
    {
        return JsonSerializer.Deserialize<SetBasketQuantitiesMessage>(payload, _serializerOptions)
        ??
        throw new JsonException("Failled to deserialize SetBasketQuantitiesMessage.");
    }
}
