namespace OrderService.api.Messages;

public class PrepareBasketResult
{
    public bool IsSuccess { get; set; } 
    public string Reason { get; set; } = string.Empty;
    public List<BasketLineDto> Items { get; set; } = new();
}
