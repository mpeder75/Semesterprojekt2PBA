namespace OrderService.api.Endpoints.OrderEndpoints;

public class ListOrdersByBuyerRequest : BaseRequest
{
    public string BuyerId { get; init; }

    public ListOrdersByBuyerRequest(string buyerId)
    {
        BuyerId = buyerId;
    }
}
