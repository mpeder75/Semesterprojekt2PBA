using CatalogService.api.DTOs;
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

    public async Task<List<CatalogItemDTO>> GetCatalogItemsById(int[] catalogItemIds)
    {
        CatalogItemsSpecification itemsSpecification = new CatalogItemsSpecification(catalogItemIds);
        List<CatalogItem> items = await _catalogRepository.ListAsync(itemsSpecification);
        List<CatalogItemDTO> dTOs = new List<CatalogItemDTO>();
        foreach (CatalogItem item in items) 
        {
            CatalogItemDTO dto = new CatalogItemDTO(item.Id, item.Name, item.Price, item.PictureUri);
            dTOs.Add(dto);
        }
        return dTOs;
    }
}
