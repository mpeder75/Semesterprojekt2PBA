namespace OrderService.api.Messages;

public class CreateOrderCommand 
{
    public string BuyerId { get; set; } = string.Empty;
    public AddressMessage ShipToAddress { get; set; } = new();
    public List<CreateOrderItemMessage> Items { get; set; } = new();
}
