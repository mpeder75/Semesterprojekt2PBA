using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;
using BasketService.api.Messaging.Replies;

namespace BasketService.api.Messaging.Handlers;

public class PrepareBasketForCheckoutHandler : IBasketMessageHandler<PrepareBasketForCheckout, IBasketReply>
{
    private readonly IBasketService _basketService;

    public PrepareBasketForCheckoutHandler(IBasketService basketService)
    {
        _basketService = basketService;
    }

    public async Task<IBasketReply> HandleAsync(PrepareBasketForCheckout message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var preparedBasket = await _basketService.PrepareBasket(message.basketId, message.buyerId);

            return new BasketPrepared(preparedBasket);
        }
        catch (Exception ex) when (
        ex is UnauthorizedAccessException 
        ||
        ex is InvalidOperationException)
        {
            return new BasketPreparationRejected(ex.Message);
        }
    }
}
