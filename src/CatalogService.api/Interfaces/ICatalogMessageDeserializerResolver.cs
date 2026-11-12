using CatalogService.api.Messaging.Envelopes;

namespace CatalogService.api.Interfaces;

public interface ICatalogMessageDeserializerResolver
{
    ICatalogMessage Deserialize(CatalogMessageEnvelope envelope);
}
