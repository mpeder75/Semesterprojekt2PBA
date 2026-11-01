using BasketService.api.Messaging.Envelopes;

namespace BasketService.api.Interfaces;

public interface IBasketMessageDeserializerResolver
{
    IBasketMessage Deserialize(BasketMessageEnvelope envelope);
}
