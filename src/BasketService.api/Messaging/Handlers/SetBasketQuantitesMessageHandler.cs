using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Handlers;

public class SetBasketQuantitesMessageHandler : IBasketMessageHandler<SetBasketQuantitiesMessage>
{
    private readonly IBasketService _basketService;

    public SetBasketQuantitesMessageHandler(IBasketService basketService)
    {
        _basketService = basketService;
    }
    public async Task HandleAsync(SetBasketQuantitiesMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _basketService.SetQuantities(message.BasketId, message.Quantities);

    }
}
