using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;
using BasketService.api.Messaging.Replies;

namespace BasketService.api.Messaging.Handlers;

public class CompleteBasketCheckoutHandler : IBasketMessageHandler<CompleteBasketCheckout, BasketCheckoutCompleted>
{
    private readonly IBasketService _basketService;

    public CompleteBasketCheckoutHandler(IBasketService basketService)
    {
        _basketService = basketService;
    }

    public async Task<BasketCheckoutCompleted> HandleAsync(CompleteBasketCheckout message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dto = await _basketService.CompleteBasketCheckout(message.basketId, message.buyerId, message.orderId);

        return new BasketCheckoutCompleted(dto);
    }
}
