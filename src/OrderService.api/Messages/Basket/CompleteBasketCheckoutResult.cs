namespace OrderService.api.Messages.Basket;

public class CompleteBasketCheckoutResult
{
    public bool IsSuccess { get; set; }
    public string Reason { get; set; } = string.Empty;
}
