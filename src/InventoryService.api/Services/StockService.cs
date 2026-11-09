using InventoryService.api.Entities;
using InventoryService.api.Interfaces;

namespace InventoryService.api.Services;

public class StockService(IInventoryRepository repository) : IStockService
{
    public Task<InventoryItem?> GetAsync(int catalogItemId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(catalogItemId);
        return repository.GetStockAsync(catalogItemId, cancellationToken);
    }

    public Task<InventoryItem> CreateAsync(int catalogItemId, int quantity, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var item = new InventoryItem(catalogItemId, quantity);
            if (await repository.GetStockForUpdateAsync(catalogItemId, cancellationToken) is not null)
                throw new InventoryConflictException("Stock is already initialized for this product.");
            repository.AddStock(item);
            return item;
        }, cancellationToken);

    public Task<InventoryItem> SetAsync(int catalogItemId, int quantity, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var item = await RequireStockAsync(catalogItemId, cancellationToken);
            ApplyStockRule(() => item.SetQuantity(quantity));
            return item;
        }, cancellationToken);

    public Task<InventoryItem> AdjustAsync(int catalogItemId, int change, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var item = await RequireStockAsync(catalogItemId, cancellationToken);
            ApplyStockRule(() => item.AdjustQuantity(change));
            return item;
        }, cancellationToken);

    public Task<bool> DeleteAsync(int catalogItemId, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var item = await RequireStockAsync(catalogItemId, cancellationToken);
            if (item.Quantity != 0 || item.ReservedQuantity != 0)
                throw new InventoryConflictException("Only zero-stock products without reserved units can be deleted.");
            repository.RemoveStock(item);
            return true;
        }, cancellationToken);

    public Task<StockReservation?> GetReservationAsync(int orderId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        return repository.GetReservationAsync(orderId, cancellationToken);
    }

    public Task<(StockReservation Reservation, bool Created)> ReserveAsync(
        int orderId, IReadOnlyCollection<ReservationItem> items, CancellationToken cancellationToken)
    {
        var requested = new StockReservation(orderId, items);
        return InTransactionAsync(async () =>
        {
            var existing = await repository.GetReservationForUpdateAsync(orderId, cancellationToken);
            if (existing is not null)
            {
                if (!existing.Matches(requested))
                    throw new InventoryConflictException("This order already has a reservation with different items.");
                // A delayed replay returns even a released reservation without allocating again.
                return (existing, false);
            }
            try
            {
                foreach (var line in requested.Items.OrderBy(x => x.CatalogItemId))
                {
                    var stock = await RequireStockAsync(line.CatalogItemId, cancellationToken);
                    ApplyStockRule(() => stock.Reserve(line.Quantity));
                }
            }
            catch (Exception error) when (error is InventoryConflictException or KeyNotFoundException)
            {
                // An identical concurrent request may have reserved these units since the first read.
                if (await repository.GetReservationAsync(orderId, cancellationToken) is not null)
                    throw new InventoryConcurrencyException("The order was reserved concurrently; reload its outcome.");
                throw;
            }
            repository.AddReservation(requested);
            return (requested, true);
        }, cancellationToken);
    }

    public Task<StockReservation> ReleaseAsync(int orderId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        return InTransactionAsync(async () =>
        {
            var reservation = await repository.GetReservationForUpdateAsync(orderId, cancellationToken)
                ?? throw new KeyNotFoundException("Reservation not found.");
            if (reservation.IsReleased)
                return reservation;

            try
            {
                foreach (var line in reservation.Items.OrderBy(x => x.CatalogItemId))
                {
                    var stock = await RequireStockAsync(line.CatalogItemId, cancellationToken);
                    ApplyStockRule(() => stock.Release(line.Quantity));
                }
            }
            catch (Exception error) when (error is InventoryConflictException or KeyNotFoundException)
            {
                if ((await repository.GetReservationAsync(orderId, cancellationToken))?.IsReleased == true)
                    throw new InventoryConcurrencyException("The order was released concurrently; reload its outcome.");
                throw;
            }
            reservation.MarkReleased();
            return reservation;
        }, cancellationToken);
    }

    private async Task<InventoryItem> RequireStockAsync(int catalogItemId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(catalogItemId);
        return await repository.GetStockForUpdateAsync(catalogItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock is not initialized for product {catalogItemId}.");
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
                var result = await action();
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (InventoryConcurrencyException)
            {
                // Disposal rolls back and clears tracking before the next attempt.
            }
        }
        throw new InventoryConflictException("Inventory changed concurrently. Please retry the operation.");
    }

    private static void ApplyStockRule(Action change)
    {
        try { change(); }
        catch (InvalidOperationException error) { throw new InventoryConflictException(error.Message); }
    }
}
