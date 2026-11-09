namespace OrderService.api.Messages.Basket;

public class PrepareBasketForCheckoutPayload
{
    public int BasketId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}
