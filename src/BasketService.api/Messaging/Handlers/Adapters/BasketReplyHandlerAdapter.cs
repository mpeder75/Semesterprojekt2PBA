using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Handlers.Adapters;

public sealed class BasketReplyHandlerAdapter<TMessage, TReply> : IBasketMessageHandlerAdapter
    where TMessage : IBasketMessage
    where TReply : IBasketReply
{
    public Type MessageType => typeof(TMessage);

    public async Task<IBasketReply?> HandleAsync(IServiceProvider serviceProvider, IBasketMessage message, CancellationToken cancellationToken = default)
    {
        var handler = serviceProvider.GetRequiredService<IBasketMessageHandler<TMessage, TReply>>();

        return await handler.HandleAsync((TMessage)message, cancellationToken);
    }
}
