using CatalogService.api.Entities;

namespace CatalogService.api.Interfaces;

public interface ICatalogService
{
    Task<List<CatalogItem>> GetCatalogItemsByIdAsync(int[] ids);
}
