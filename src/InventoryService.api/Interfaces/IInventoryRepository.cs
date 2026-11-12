using InventoryService.api.Entities;

namespace InventoryService.api.Interfaces;

public interface IInventoryRepository
{
    Task<InventoryItem?> GetStockAsync(int catalogItemId, CancellationToken cancellationToken);
    // For-update reads return tracked entities. RowVersion is checked when the transaction is saved.
    Task<InventoryItem?> GetStockForUpdateAsync(int catalogItemId, CancellationToken cancellationToken);
    Task<StockReservation?> GetReservationAsync(int orderId, CancellationToken cancellationToken);
    Task<StockReservation?> GetReservationForUpdateAsync(int orderId, CancellationToken cancellationToken);
    Task<bool> HasReservationForAnotherOrderAsync(Guid checkoutId, int orderId, CancellationToken cancellationToken);
    // Changes are persisted together by IInventoryTransaction.CommitAsync.
    void AddStock(InventoryItem item);
    void RemoveStock(InventoryItem item);
    void AddReservation(StockReservation reservation);
    Task<IInventoryTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
