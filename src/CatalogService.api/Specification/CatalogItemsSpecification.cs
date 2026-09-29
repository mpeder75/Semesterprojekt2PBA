using Ardalis.Specification;
using CatalogService.api.Entities;

namespace CatalogService.api.Specification;

public class CatalogItemsSpecification : Specification<CatalogItem>
{
    public CatalogItemsSpecification(params int[] ids)
    {
        Query.Where(c => ids.Contains(c.Id));
    }
}
