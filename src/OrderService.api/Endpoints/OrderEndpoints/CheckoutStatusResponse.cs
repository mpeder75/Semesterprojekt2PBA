using OrderService.api.Interfaces;

namespace OrderService.api.Endpoints.OrderEndpoints;

public class CheckoutStatusResponse : BaseResponse, IAggregateRoot
{
    public Guid CheckoutId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string State { get; set; } = string.Empty;

    public CheckoutStatusResponse(Guid correlationId) : base(correlationId)
    {

    }

    public CheckoutStatusResponse()
    {
        
    }
}
