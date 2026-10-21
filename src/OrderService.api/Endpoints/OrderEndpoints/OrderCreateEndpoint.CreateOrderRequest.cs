namespace OrderService.api.Endpoints.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public AddressDto ShipToAddress { get; set; } = new();
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}
