namespace OrderService.api.Endpoints.OrderEndpoints;

public class GetByIdOrderResponse : BaseResponse
{
    public GetByIdOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetByIdOrderResponse()
    {
    }

    public OrderDetailDto Order { get; set; } = new();
}
