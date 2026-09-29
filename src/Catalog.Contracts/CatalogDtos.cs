using System.Collections.Generic;

namespace Microsoft.eShopWeb.Catalog.Contracts;

public record CatalogItemDto(int Id, string Name, string Description, decimal Price,
    string PictureUri, int CatalogBrandId, int CatalogTypeId);

public record CatalogBrandDto(int Id, string Brand);

public record CatalogTypeDto(int Id, string Type);

public record CatalogPage(IReadOnlyList<CatalogItemDto> Items, int TotalItems);

public record CatalogItemWrite(string Name, string Description, decimal Price,
    int CatalogBrandId, int CatalogTypeId);

public record CatalogSnapshot(IReadOnlyList<CatalogBrandDto> Brands,
    IReadOnlyList<CatalogTypeDto> Types, IReadOnlyList<CatalogItemDto> Items);
