namespace BasketService.api.Interfaces;

public interface IBasketMessageHandler<in TMessage> 
    where TMessage : IBasketMessage
{
    Task HandleAsync( TMessage message, CancellationToken cancellationToken = default);
}

public interface IBasketMessageHandler<in TMessage, TReply>
    where TMessage : IBasketMessage
    where TReply : IBasketReply
{
    Task<TReply> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
