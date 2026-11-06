namespace OrderService.api.Endpoints.OrderEndpoints;

public class StartCheckoutRequest : BaseRequest
{
    public int BasketId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public AddressDto ShipToAddress { get; set; } = new();

    // checkoutId er ikke med som property, den kommer i header
}
