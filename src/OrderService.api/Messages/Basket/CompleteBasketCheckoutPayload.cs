namespace OrderService.api.Messages.Basket;

public class CompleteBasketCheckoutPayload
{
    public int BasketId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public int OrderId { get; set; }
}
