using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;
using BasketService.api.Messaging.Replies;

namespace BasketService.api.Messaging.Handlers;

public class GetBasketByIdMessageHandler : IBasketMessageHandler<GetBasketByIdMessage, GetBasketByIdReply>
{
    private readonly IBasketService _basketService;

    public GetBasketByIdMessageHandler(IBasketService basketService)
    {
        _basketService = basketService;
    }
    public async Task<GetBasketByIdReply> HandleAsync(GetBasketByIdMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var basket = await _basketService.GetBasketByIdAsync(message.basketId);

        return new GetBasketByIdReply(basket);
    }
}
