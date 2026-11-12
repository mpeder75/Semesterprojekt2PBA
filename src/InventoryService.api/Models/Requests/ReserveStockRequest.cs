namespace InventoryService.api.Models.Requests;

public sealed record ReserveStockRequest(int? OrderId, List<ReservationItemRequest?>? Items);
