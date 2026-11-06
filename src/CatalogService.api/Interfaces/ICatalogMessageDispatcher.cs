namespace CatalogService.api.Interfaces;

public interface ICatalogMessageDispatcher
{
    Task<ICatalogReply> DispatchRequestAsync(ICatalogMessage message, CancellationToken cancellationToken = default);
}
