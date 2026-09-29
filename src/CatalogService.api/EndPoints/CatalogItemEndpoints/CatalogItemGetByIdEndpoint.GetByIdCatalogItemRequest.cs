namespace CatalogService.api.EndPoints.CatalogItemEndpoints;

public class GetByIdCatalogItemRequest : BaseRequest
{
    public int CatalogItemId { get; init; }

    public GetByIdCatalogItemRequest(int catalogItemId)
    {
        CatalogItemId = catalogItemId;
    }
}
