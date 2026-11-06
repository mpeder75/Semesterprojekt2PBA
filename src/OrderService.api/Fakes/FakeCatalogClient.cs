using OrderService.api.Interfaces;
using OrderService.api.Messages;

namespace OrderService.api.Fakes;

public class FakeCatalogClient : ICatalogClient
{
    public Task<GetCatalogItemsResult> GetCatalogItemsByIdsAsync(List<int> catalogItemIds)
    {
        return Task.FromResult(new GetCatalogItemsResult
        {
            IsSuccess = true,
            Reason = string.Empty,
            Items = new List<CatalogItemDto>
            {
                new CatalogItemDto { CatalogItemId = 1, Name = "Item 1", Price = 10.00m, PictureUri = "https://example.com/item1.jpg" },
                new CatalogItemDto { CatalogItemId = 2, Name = "Item 2", Price = 20.00m, PictureUri = "https://example.com/item2.jpg" }
            }
        });
    }
}
