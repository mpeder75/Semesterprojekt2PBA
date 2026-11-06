namespace OrderService.api.Messages;

public class ReserveStockResult
{
    public bool IsSuccess { get; set; }
    public string Reason { get; set; } = string.Empty;

}
