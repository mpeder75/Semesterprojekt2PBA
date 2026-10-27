using BasketService.api.Interfaces;
using BasketService.api.Messaging.Messages;

namespace BasketService.api.Messaging.Handlers;

public class DeleteBasketMessageHandler : IBasketMessageHandler<DeleteBasketMessage>
{
    private readonly IBasketService _basketService;

    public DeleteBasketMessageHandler(IBasketService basketService)
    {
        _basketService = basketService;
    }

    public async Task HandleAsync(DeleteBasketMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _basketService.DeleteBasketAsync(message.BasketId);
    }
}
