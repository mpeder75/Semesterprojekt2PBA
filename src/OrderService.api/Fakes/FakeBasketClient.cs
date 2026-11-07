using OrderService.api.Interfaces;
using OrderService.api.Messages.Basket;

namespace OrderService.api.Fakes;

public class FakeBasketClient : IBasketClient
{
    public Task<PrepareBasketResult> PrepareBasketForCheckoutAsync(int basketId, string buyerId)
    {
       return Task.FromResult(new PrepareBasketResult
       {
           IsSuccess = true,
           Reason = string.Empty,
           Items = new List<BasketLineDto>
           {
               new BasketLineDto { CatalogItemId = 1, Quantity = 2 },
               new BasketLineDto { CatalogItemId = 2, Quantity = 1 }
           }
       });
    }
    public Task<CompleteBasketCheckoutResult> CompleteBasketCheckoutAsync(int basketId, Guid checkoutId)
    {
        return Task.FromResult(new CompleteBasketCheckoutResult
        {
            IsSuccess = true,
            Reason = string.Empty
        });
    }

    public Task<ReleaseBasketCheckoutResult> ReleaseBasketCheckoutAsync(int basketId, Guid checkoutId)
    {
        return Task.FromResult(new ReleaseBasketCheckoutResult
        {
            IsSuccess = true,
            Reason = string.Empty
        });
    }
}
