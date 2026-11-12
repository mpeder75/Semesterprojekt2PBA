namespace OrderService.api.Messages.Inventory;

public class StockReservationStatusPayload
{
    public int OrderId { get; init; }
    public Guid? CheckoutId { get; init; }
    public string? Status { get; init; }
}
