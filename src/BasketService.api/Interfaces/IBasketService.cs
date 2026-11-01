using Ardalis.Result;
using BasketService.api.Entities.BasketAggregate;

namespace BasketService.api.Interfaces;

public interface IBasketService
{
    Task TransferBasketAsync(string anonmousId, string userName);
    Task<Basket> AddItemToBasket(string userName, int catalogItemId, decimal price, int quantity = 1);
    Task<Result<Basket>> SetQuantities(int basketId, Dictionary<string, int> quantities);
    Task<bool> DeleteBasketAsync(int basketId);
    Task<Basket> GetBasketByIdAsync(int basketId);
}
