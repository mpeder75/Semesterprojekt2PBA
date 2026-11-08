using System.Text.Json;
using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Serialization;

public class CatalogMessageSerializer : ICatalogMessageSerializer
{
    private readonly JsonSerializerOptions _option;

    public CatalogMessageSerializer()
    {
        _option = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string Serialize(ICatalogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return JsonSerializer.Serialize(message, message.GetType(), _option);
    }
}
