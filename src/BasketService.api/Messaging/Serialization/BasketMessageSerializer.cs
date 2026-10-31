using System.Text.Json;
using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Serialization;

public class BasketMessageSerializer : IBasketMessageSerializer
{
    private readonly JsonSerializerOptions _option;

    public BasketMessageSerializer()
    {
        _option = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string Serialize(IBasketMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return JsonSerializer.Serialize(message, message.GetType(), _option);
    }
}
