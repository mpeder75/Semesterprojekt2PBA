using System.Text.Json;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Deserialization;

public class GetBasketByIdMessageDeserializer : IBasketMessageDeserialzier<GetBasketByIdMessage>
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public GetBasketByIdMessageDeserializer()
    {
        _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string MessageType => nameof(GetBasketByIdMessage);

    public GetBasketByIdMessage Deserialize(string payload)
    {
        return JsonSerializer.Deserialize<GetBasketByIdMessage>(payload, _jsonSerializerOptions)
        ??
        throw new JsonException("Failled to deserialize GetBasketByIdMessage");
    }
}
