namespace BasketService.api.Interfaces;

public interface IBasketMessageHandler<in TMessage> where TMessage : IBasketMessage
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}

public interface IBasketMessageHandler<in TMessage, TResult> where TMessage : IBasketMessage
{
    Task<TResult> HandleAsync( TMessage message, CancellationToken cancellationToken = default);
}
