namespace CatalogService.api.Interfaces;

public interface ICatalogMessageDeserializer
{
    ICatalogMessage Deserialize(string payload, Type messageType);
}
