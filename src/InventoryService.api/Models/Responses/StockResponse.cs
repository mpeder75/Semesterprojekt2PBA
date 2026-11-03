namespace InventoryService.api.Models.Responses;

public sealed record StockResponse(int CatalogItemId, int Quantity, int ReservedQuantity, int AvailableQuantity);
