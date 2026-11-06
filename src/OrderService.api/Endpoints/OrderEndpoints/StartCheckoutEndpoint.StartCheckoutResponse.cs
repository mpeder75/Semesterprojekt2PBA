namespace OrderService.api.Endpoints.OrderEndpoints;

public class StartCheckoutResponse : BaseResponse
{
    public Guid CheckoutId { get; set; }
    public string StatusUrl { get; set; } = string.Empty;

    public StartCheckoutResponse(Guid correlationId) : base(correlationId)
    {

    }

    public StartCheckoutResponse()
    {
        
    }
}
