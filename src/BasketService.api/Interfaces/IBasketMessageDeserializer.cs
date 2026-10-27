namespace BasketService.api.Interfaces;

public interface IBasketMessageDeserializer
{
    string MessageType { get; }

    IBasketMessage Deserialize(string payload);
}
