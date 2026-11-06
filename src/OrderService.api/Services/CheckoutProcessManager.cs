using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Specifications;

namespace OrderService.api.Services;

public class CheckoutProcessManager
{
    private readonly IRepository<CheckoutProcess> _repository;
    private readonly IBasketClient _basketClient;

    public CheckoutProcessManager(IRepository<CheckoutProcess> repository, IBasketClient basketClient)
    {
        _repository = repository;
        _basketClient = basketClient;
    }

    public async Task AdvanceOrderAsync(Guid checkoutId)
    {
        // Find rækken
        var checkout = await _repository.FirstOrDefaultAsync(new CheckoutProcessByCheckoutIdSpec(checkoutId));

        if (checkout is null || checkout.State != CheckoutState.AwaitingBasket)
        {
            return;
        }

        // Spørg basket
        var result = await _basketClient.PrepareBasketForCheckoutAsync(checkout.BasketId, checkout.BuyerId);

        if (!result.IsSuccess)
        {
            return;
        }

        // opdater tilstanden
        checkout.MoveTo(CheckoutState.AwaitingCatalog);

        // Gem
        await _repository.UpdateAsync(checkout);



    }


}
