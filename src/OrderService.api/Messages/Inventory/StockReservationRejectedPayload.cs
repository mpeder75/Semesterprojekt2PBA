namespace OrderService.api.Messages.Inventory;

public class StockReservationRejectedPayload
{
    public string Reason { get; set; } = string.Empty;
}
