namespace OrderService.api.Endpoints.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUrl { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
