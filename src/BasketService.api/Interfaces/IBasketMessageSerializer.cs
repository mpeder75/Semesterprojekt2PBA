namespace BasketService.api.Interfaces;

public interface IBasketMessageSerializer
{
    string Serialize (IBasketMessage message);
}
