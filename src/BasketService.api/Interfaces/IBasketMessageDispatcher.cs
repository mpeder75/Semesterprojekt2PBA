namespace BasketService.api.Interfaces;

public interface IBasketMessageDispatcher
{
    Task DispatchMessageAsync(
        IBasketMessage message,
        CancellationToken cancellationToken = default);

    Task<IBasketReply> DispatchRequestAsync(IBasketMessage message, CancellationToken cancellationToken = default);
}
