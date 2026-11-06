using OrderService.api.Messages;

namespace OrderService.api.Interfaces;

public interface IBasketClient
{
    public Task<PrepareBasketResult> PrepareBasketForCheckoutAsync(int basketId, string buyerId);

}
