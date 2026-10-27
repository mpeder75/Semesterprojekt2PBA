using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Handlers;

public class TransferBasketMessageHandler : IBasketMessageHandler<TransferBasketMessage>
{
    private readonly IBasketService _basketService;

    public TransferBasketMessageHandler(IBasketService basketService)
    {
        _basketService = basketService;
    }
    public async Task HandleAsync(TransferBasketMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _basketService.TransferBasketAsync(message.AnonmousId, message.Username);
    }
}
