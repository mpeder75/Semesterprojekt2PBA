using OrderService.api.Messages;

namespace OrderService.api.Interfaces;

public interface IBasketClient
{
    public Task<PrepareBasketResult> PrepareBasketForCheckoutAsync(int basketId, string buyerId);
    public Task<CompleteBasketCheckoutResult> CompleteBasketCheckoutAsync(int basketId, Guid checkoutId);
    public Task<ReleaseBasketCheckoutResult> ReleaseBasketCheckoutAsync(int basketId, Guid checkoutId);

}
