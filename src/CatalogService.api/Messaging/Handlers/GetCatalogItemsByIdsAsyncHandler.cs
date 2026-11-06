using CatalogService.api.Interfaces;
using CatalogService.api.Messaging.Messages;
using CatalogService.api.Messaging.Replies;

namespace CatalogService.api.Messaging.Handlers;

public class GetCatalogItemsByIdsAsyncHandler : ICatalogMessageHandler<GetCatalogItemsByIdAsyncMessage, GetCatalogItemsByIdsAsyncReply>
{
    private readonly ICatalogService _catalogService;

    public GetCatalogItemsByIdsAsyncHandler(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<GetCatalogItemsByIdsAsyncReply> HandleAsync(GetCatalogItemsByIdAsyncMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var catalogItems = await _catalogService.GetCatalogItemsByIdAsync(message.ids);

        return new GetCatalogItemsByIdsAsyncReply(catalogItems);
    }
}
