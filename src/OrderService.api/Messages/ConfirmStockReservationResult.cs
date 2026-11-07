namespace OrderService.api.Messages;

public class ConfirmStockReservationResult
{
    public bool IsSuccess { get; set; }
    public string Reason { get; set; } = string.Empty;
}
