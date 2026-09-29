using Ardalis.Specification;
using CatalogService.api.Entities;

namespace CatalogService.api.Specification;

public class CatalogItemNameSpecification : Specification<CatalogItem>
{
    public CatalogItemNameSpecification(string catalogItemName)
    {
        Query.Where(item => catalogItemName == item.Name);
    }
}
