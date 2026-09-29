namespace Microsoft.eShopWeb.Catalog.Contracts;

public static class CatalogValidation
{
    public static void Validate(CatalogItemWrite item)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 50 ||
            string.IsNullOrWhiteSpace(item.Description) || item.Price <= 0 ||
            item.CatalogBrandId <= 0 || item.CatalogTypeId <= 0)
            throw new ArgumentException("A name (up to 50 characters), description, positive price, brand and type are required.");
    }

    public static void ValidatePage(int pageIndex, int pageSize)
    {
        // Zero preserves the existing PublicApi convention: return all matching items.
        if (pageIndex < 0 || pageSize < 0 || (long)pageIndex * pageSize > int.MaxValue)
            throw new ArgumentException("Page index and size must be non-negative and within range.");
    }
}
