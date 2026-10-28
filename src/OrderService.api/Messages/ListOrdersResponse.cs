namespace OrderService.api.Messages;

public class ListOrdersResponse
{
    public List<OrderSummaryMessage> Orders { get; set; } = new();
}
