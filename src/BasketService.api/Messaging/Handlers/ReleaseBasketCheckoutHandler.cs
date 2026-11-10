using BasketService.api.Entities.DTOs;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;
using BasketService.api.Messaging.Replies;

namespace BasketService.api.Messaging.Handlers;

public class ReleaseBasketCheckoutHandler : IBasketMessageHandler<ReleaseBasketCheckout, BasketCheckoutReleased>
{
    public async Task<BasketCheckoutReleased> HandleAsync(ReleaseBasketCheckout message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EmptyDto dto = new EmptyDto();
        return new BasketCheckoutReleased(dto);
    }
}
