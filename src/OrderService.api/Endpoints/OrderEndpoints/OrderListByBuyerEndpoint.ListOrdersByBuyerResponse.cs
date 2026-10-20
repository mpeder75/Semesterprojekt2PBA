namespace OrderService.api.Endpoints.OrderEndpoints;

public class ListOrdersByBuyerResponse : BaseResponse
{
    public ListOrdersByBuyerResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListOrdersByBuyerResponse()
    {
    }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}
