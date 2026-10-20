namespace OrderService.api.Endpoints.OrderEndpoints;

public class GetByIdOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public GetByIdOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}
