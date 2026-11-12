namespace CatalogService.api.Interfaces;

public interface ICatalogReplyHandlerAdapter
{
    Type MessageType { get;  }

    Task<ICatalogReply?> HandleAsync(
        IServiceProvider serviceProvider,
        ICatalogMessage message,
        CancellationToken cancellationToken);
}
