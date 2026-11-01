namespace BasketService.api.Interfaces;

public interface IBasketMessageDeserializer
{
    IBasketMessage Deserialize(string payload, Type messageType);
}
