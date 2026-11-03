namespace InventoryService.api.Models.Requests;

public sealed record AdjustStockRequest(int? QuantityChange);
