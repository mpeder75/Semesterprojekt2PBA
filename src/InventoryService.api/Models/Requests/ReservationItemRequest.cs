namespace InventoryService.api.Models.Requests;

public sealed record ReservationItemRequest(int? CatalogItemId, int? Quantity);
