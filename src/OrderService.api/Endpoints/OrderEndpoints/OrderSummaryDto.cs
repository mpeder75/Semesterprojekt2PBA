namespace OrderService.api.Endpoints.OrderEndpoints;

public class OrderSummaryDto
{
    public int Id { get; set; }
    public AddressDto Address { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}
