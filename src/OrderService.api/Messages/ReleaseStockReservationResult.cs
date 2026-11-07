namespace OrderService.api.Messages;

public class ReleaseStockReservationResult
{
    public bool IsSuccess { get; set; }
    public string Reason { get; set; } = string.Empty;
}
