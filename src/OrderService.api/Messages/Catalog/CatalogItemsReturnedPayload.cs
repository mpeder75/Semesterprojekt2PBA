namespace OrderService.api.Messages.Catalog;

public class CatalogItemsReturnedPayload
{
    public List<CatalogItemDto> Items { get; set; } = new();
}
