namespace InventoryService.api.Models.Requests;

public sealed record CreateStockRequest(int? CatalogItemId, int? Quantity);
