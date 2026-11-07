using OrderService.api.Messages.Catalog;

namespace OrderService.api.Interfaces;

public interface ICatalogClient
{
    public Task<GetCatalogItemsResult> GetCatalogItemsByIdsAsync(List<int> catalogItemIds);
}
