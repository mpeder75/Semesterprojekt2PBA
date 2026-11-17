using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;
using BasketService.api.Messaging.Replies;

namespace BasketService.api.Messaging.Handlers;

public class PrepareBasketForCheckoutHandler : IBasketMessageHandler<PrepareBasketForCheckout, IBasketReply>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<PrepareBasketForCheckoutHandler> _logger;

    public PrepareBasketForCheckoutHandler(IBasketService basketService, ILogger<PrepareBasketForCheckoutHandler> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task<IBasketReply> HandleAsync(PrepareBasketForCheckout message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var preparedBasket = await _basketService.PrepareBasket(message.basketId, message.buyerId);

            return new BasketPrepared(preparedBasket);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Checkout preparation was denied for basket {BasketId}", message.basketId);
            return new BasketPreparationRejected(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checkout preparation failed for basket {BasketId}", message.basketId);
            return new BasketPreparationRejected(ex.Message);
        }
    }
}
