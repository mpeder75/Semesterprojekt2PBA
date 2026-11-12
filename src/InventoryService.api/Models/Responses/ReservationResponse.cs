namespace InventoryService.api.Models.Responses;

public sealed record ReservationResponse(int OrderId, string Status, IReadOnlyList<ReservationItemResponse> Items);
