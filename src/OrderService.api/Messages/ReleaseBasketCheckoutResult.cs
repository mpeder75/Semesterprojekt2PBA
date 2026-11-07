namespace OrderService.api.Messages;

public class ReleaseBasketCheckoutResult
{
    public bool IsSuccess { get; set; }
    public string Reason { get; set; } = string.Empty;
}
