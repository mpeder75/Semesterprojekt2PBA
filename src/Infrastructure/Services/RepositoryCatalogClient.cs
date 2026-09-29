using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Catalog.Contracts;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Legacy implementation retained for the feature toggle's OFF path during migration.
/// </summary>
public class RepositoryCatalogClient : ICatalogClient
{
    private readonly IRepository<CatalogItem> _items;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly IReadRepository<CatalogType> _types;

    public RepositoryCatalogClient(IRepository<CatalogItem> items,
        IReadRepository<CatalogBrand> brands, IReadRepository<CatalogType> types)
    {
        _items = items;
        _brands = brands;
        _types = types;
    }

    public async Task<CatalogPage> GetItemsAsync(int pageIndex, int pageSize, int? brandId,
        int? typeId, CancellationToken cancellationToken = default)
    {
        CatalogValidation.ValidatePage(pageIndex, pageSize);
        var items = await _items.ListAsync(
            new CatalogFilterPaginatedSpecification(pageIndex * pageSize, pageSize, brandId, typeId), cancellationToken);
        var totalItems = await _items.CountAsync(new CatalogFilterSpecification(brandId, typeId), cancellationToken);
        return new CatalogPage(items.Select(ToDto).ToList(), totalItems);
    }

    public async Task<CatalogItemDto?> GetItemByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await _items.GetByIdAsync(id, cancellationToken);
        return item is null ? null : ToDto(item);
    }

    public async Task<IReadOnlyList<CatalogItemDto>> GetItemsByIdsAsync(int[] ids, CancellationToken cancellationToken = default)
    {
        if (ids.Length == 0)
        {
            return new List<CatalogItemDto>();
        }

        var items = await _items.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<CatalogBrandDto>> GetBrandsAsync(CancellationToken cancellationToken = default)
    {
        var brands = await _brands.ListAsync(cancellationToken);
        return brands.Select(brand => new CatalogBrandDto(brand.Id, brand.Brand)).ToList();
    }

    public async Task<IReadOnlyList<CatalogTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = await _types.ListAsync(cancellationToken);
        return types.Select(type => new CatalogTypeDto(type.Id, type.Type)).ToList();
    }

    private static CatalogItemDto ToDto(CatalogItem item) => new(item.Id, item.Name,
        item.Description, item.Price, item.PictureUri, item.CatalogBrandId, item.CatalogTypeId);

    public async Task<CatalogItemDto> CreateItemAsync(CatalogItemWrite request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        if (await _items.CountAsync(new CatalogItemNameSpecification(request.Name), cancellationToken) > 0)
            throw new DuplicateException($"A catalogItem with name {request.Name} already exists");
        var item = new CatalogItem(request.CatalogTypeId, request.CatalogBrandId,
            request.Description, request.Name, request.Price, "/images/products/eCatalog-item-default.png");
        await _items.AddAsync(item, cancellationToken);
        return ToDto(item);
    }

    public async Task<CatalogItemDto?> UpdateItemAsync(int id, CatalogItemWrite request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var item = await _items.GetByIdAsync(id, cancellationToken);
        if (item is null) return null;
        item.UpdateDetails(new CatalogItem.CatalogItemDetails(request.Name, request.Description, request.Price));
        item.UpdateBrand(request.CatalogBrandId);
        item.UpdateType(request.CatalogTypeId);
        await _items.UpdateAsync(item, cancellationToken);
        return ToDto(item);
    }

    public async Task<bool> DeleteItemAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await _items.GetByIdAsync(id, cancellationToken);
        if (item is null) return false;
        await _items.DeleteAsync(item, cancellationToken);
        return true;
    }

    private async Task ValidateAsync(CatalogItemWrite request, CancellationToken token)
    {
        CatalogValidation.Validate(request);
        if (await _brands.GetByIdAsync(request.CatalogBrandId, token) is null ||
            await _types.GetByIdAsync(request.CatalogTypeId, token) is null)
            throw new System.ArgumentException("Unknown catalog brand or type.");
    }
}
