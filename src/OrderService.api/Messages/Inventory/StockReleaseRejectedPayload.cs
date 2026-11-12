namespace OrderService.api.Messages.Inventory;

public sealed record StockReleaseRejectedPayload(int OrderId, string Code, string Reason);
