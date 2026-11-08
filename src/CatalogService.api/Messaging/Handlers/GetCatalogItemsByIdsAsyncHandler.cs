using CatalogService.api.Interfaces;
using CatalogService.api.Messaging.Messages;
using CatalogService.api.Messaging.Replies;

namespace CatalogService.api.Messaging.Handlers;

public class GetCatalogItemsByIdsAsyncHandler : ICatalogMessageHandler<GetCatalogItemsByIdsAsyncMessage, GetCatalogItemsByIdsAsyncReply>
{
    private readonly ICatalogService _catalogService;

    public GetCatalogItemsByIdsAsyncHandler(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<GetCatalogItemsByIdsAsyncReply> HandleAsync(GetCatalogItemsByIdsAsyncMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var catalogItems = await _catalogService.GetCatalogItemsByIdAsync(message.catalogItemIds);

        return new GetCatalogItemsByIdsAsyncReply(catalogItems);
    }
}
