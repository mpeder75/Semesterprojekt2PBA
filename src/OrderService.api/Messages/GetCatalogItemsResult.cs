namespace OrderService.api.Messages;

public class GetCatalogItemsResult
{
    public bool IsSuccess { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<CatalogItemDto> Items { get; set; } = new();

}
