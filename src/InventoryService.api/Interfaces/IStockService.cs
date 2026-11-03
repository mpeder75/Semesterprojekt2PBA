using InventoryService.api.Entities;

namespace InventoryService.api.Interfaces;

public interface IStockService
{
    Task<InventoryItem?> GetAsync(int catalogItemId, CancellationToken cancellationToken);
    Task<InventoryItem> CreateAsync(int catalogItemId, int quantity, CancellationToken cancellationToken);
    Task<InventoryItem> SetAsync(int catalogItemId, int quantity, CancellationToken cancellationToken);
    Task<InventoryItem> AdjustAsync(int catalogItemId, int change, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int catalogItemId, CancellationToken cancellationToken);
    Task<StockReservation?> GetReservationAsync(int orderId, CancellationToken cancellationToken);
    Task<(StockReservation Reservation, bool Created)> ReserveAsync(
        int orderId, IReadOnlyCollection<ReservationItem> items, CancellationToken cancellationToken);
    Task<StockReservation> ReleaseAsync(int orderId, CancellationToken cancellationToken);
}
