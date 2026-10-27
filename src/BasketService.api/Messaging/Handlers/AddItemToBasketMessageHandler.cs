using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Handlers;

public class AddItemToBasketMessageHandler : IBasketMessageHandler<AddItemToBasketMessage>
{
    private readonly IBasketService _basketService;

    public AddItemToBasketMessageHandler(IBasketService basketService)
    {
        _basketService = basketService;
    }

    public async Task HandleAsync(AddItemToBasketMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _basketService.AddItemToBasket(
            message.Username,
            message.CatalogItemid,
            message.price,
            message.Quantity);
    }
}
