namespace OrderService.api.Messages.Inventory;

public class ReserveStockPayload
{
    public int OrderId { get; set; }
    public List<StockLineDto> Items { get; set; } = new();
}
