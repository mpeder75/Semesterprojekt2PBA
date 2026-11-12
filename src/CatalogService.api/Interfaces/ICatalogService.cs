using CatalogService.api.DTOs;

namespace CatalogService.api.Interfaces;

public interface ICatalogService
{
    Task<List<CatalogItemDTO>> GetCatalogItemsById(int[] ids);
}
