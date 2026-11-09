namespace OrderService.api.Messages.Basket;

public class ReleaseBasketCheckoutPayload
{
    public int BasketId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
