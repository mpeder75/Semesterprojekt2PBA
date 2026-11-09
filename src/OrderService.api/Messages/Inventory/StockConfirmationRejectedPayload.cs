namespace OrderService.api.Messages.Inventory;

public class StockConfirmationRejectedPayload
{
    public string Reason { get; set; } = string.Empty;
}
