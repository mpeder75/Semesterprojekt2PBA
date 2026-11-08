using CatalogService.api.Entities;
using CatalogService.api.Interfaces;
using CatalogService.api.Specification;

namespace CatalogService.api.Services;

public class CatalogService : ICatalogService
{
    private readonly IRepository<CatalogItem> _catalogRepository;

    public CatalogService(IRepository<CatalogItem> catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<List<CatalogItem>> GetCatalogItemsByIdAsync(int[] catalogItemIds)
    {
        CatalogItemsSpecification itemsSpecification = new CatalogItemsSpecification(catalogItemIds);
        List<CatalogItem> items = await _catalogRepository.ListAsync(itemsSpecification);
        return items;
    }
}
