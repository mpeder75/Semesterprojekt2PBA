namespace OrderService.api.Messages.Catalog;

public class GetCatalogItemsByIdsPayload
{
    public List<int> CatalogItemIds { get; set; } = new();
}
