namespace OrderService.api.Messages.Inventory;

public class ReleaseStockReservationPayload
{
    public int OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
