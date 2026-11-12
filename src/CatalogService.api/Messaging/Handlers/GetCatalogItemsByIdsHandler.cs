using CatalogService.api.Interfaces;
using CatalogService.api.Messaging.Messages;
using CatalogService.api.Messaging.Replies;

namespace CatalogService.api.Messaging.Handlers;

public class GetCatalogItemsByIdsHandler : ICatalogMessageHandler<GetCatalogItemsByIdsMessage, CatalogItemsReturned>
{
    private readonly ICatalogService _catalogService;

    public GetCatalogItemsByIdsHandler(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<CatalogItemsReturned> HandleAsync(GetCatalogItemsByIdsMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var catalogItems = await _catalogService.GetCatalogItemsById(message.catalogItemIds);

        return new CatalogItemsReturned(catalogItems);
    }
}
