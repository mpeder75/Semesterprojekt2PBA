using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Handlers.Adapters;

public class CatalogReplyHandlerAdapter<TMessage, TReply> : ICatalogReplyHandlerAdapter
    where TMessage : ICatalogMessage
    where TReply : ICatalogReply
{
    public Type MessageType => typeof(TMessage);

    public async Task<ICatalogReply?> HandleAsync(IServiceProvider serviceProvider, ICatalogMessage message, CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetRequiredService<ICatalogMessageHandler<TMessage, TReply>>();

        return await handler.HandleAsync((TMessage)message, cancellationToken);
    }
}
