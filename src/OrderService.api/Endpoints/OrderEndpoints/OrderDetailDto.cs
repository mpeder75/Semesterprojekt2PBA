namespace OrderService.api.Endpoints.OrderEndpoints;

public class OrderDetailDto
{
    public int Id { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public AddressDto Address { get; set; } = new();
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderItemDto> OrderItems { get; set; } = new();
}
