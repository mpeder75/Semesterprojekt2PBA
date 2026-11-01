namespace BasketService.api.Interfaces;

public interface IBasketMessageHandlerAdapter
{
    Type MessageType { get; }

    Task<IBasketReply?> HandleAsync(
        IServiceProvider serviceProvider,
        IBasketMessage message,
        CancellationToken cancellationToken = default);
}
