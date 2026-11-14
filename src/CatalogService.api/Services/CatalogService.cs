using CatalogService.api.DTOs;
using CatalogService.api.Entities;
using CatalogService.api.Interfaces;
using CatalogService.api.Specification;

namespace CatalogService.api.Services;

public class CatalogService : ICatalogService
{
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IUriComposer _uriComposer;

    public CatalogService(IRepository<CatalogItem> catalogRepository, IUriComposer uriComposer)
    {
        _catalogRepository = catalogRepository;
        _uriComposer = uriComposer;
    }

    public async Task<List<CatalogItemDTO>> GetCatalogItemsById(int[] catalogItemIds)
    {
        CatalogItemsSpecification itemsSpecification = new CatalogItemsSpecification(catalogItemIds);
        List<CatalogItem> items = await _catalogRepository.ListAsync(itemsSpecification);
        List<CatalogItemDTO> dTOs = new List<CatalogItemDTO>();
        foreach (CatalogItem item in items) 
        {
            // Billedadressen sendes færdig sammensat, så modtageren ikke skal kende Catalogs pladsholder
            CatalogItemDTO dto = new CatalogItemDTO(item.Id, item.Name, item.Price, _uriComposer.ComposePicUri(item.PictureUri));
            dTOs.Add(dto);
        }
        return dTOs;
    }
}
