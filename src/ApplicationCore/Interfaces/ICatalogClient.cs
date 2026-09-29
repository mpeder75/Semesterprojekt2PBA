using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Catalog.Contracts;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Catalog boundary. Callers receive data, not persistence entities or UI models.
/// </summary>
public interface ICatalogClient
{
    Task<CatalogPage> GetItemsAsync(int pageIndex, int pageSize, int? brandId, int? typeId, CancellationToken cancellationToken = default);
    Task<CatalogItemDto?> GetItemByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogItemDto>> GetItemsByIdsAsync(int[] ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogBrandDto>> GetBrandsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default);
    Task<CatalogItemDto> CreateItemAsync(CatalogItemWrite item, CancellationToken cancellationToken = default);
    Task<CatalogItemDto?> UpdateItemAsync(int id, CatalogItemWrite item, CancellationToken cancellationToken = default);
    Task<bool> DeleteItemAsync(int id, CancellationToken cancellationToken = default);
}
