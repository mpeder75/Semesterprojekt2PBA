namespace BasketService.api.Interfaces;

public interface IBasketMessageDeserialzier<TMessage> : IBasketMessageDeserializer 
    where TMessage : IBasketMessage
{
    new TMessage Deserialize(string payload);

    IBasketMessage IBasketMessageDeserializer.Deserialize(string payload)
    {
        return Deserialize(payload);
    }
}
