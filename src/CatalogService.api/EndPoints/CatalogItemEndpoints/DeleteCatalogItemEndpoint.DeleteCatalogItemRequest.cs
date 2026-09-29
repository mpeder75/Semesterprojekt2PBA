using CatalogService.api;

namespace CatalogService.api.EndPoints.CatalogItemEndpoints;

public class DeleteCatalogItemRequest : BaseRequest
{
    public int CatalogItemId { get; init; }

    public DeleteCatalogItemRequest(int catalogItemId)
    {
        CatalogItemId = catalogItemId;
    }
}
