namespace CatalogService.api.Interfaces;

public interface ICatalogMessageHandler<in TMessage>
    where TMessage : ICatalogMessage
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}

public interface ICatalogMessageHandler<in TMessage, TReply>
    where TMessage : ICatalogMessage
    where TReply : ICatalogReply
{
    Task<TReply> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
