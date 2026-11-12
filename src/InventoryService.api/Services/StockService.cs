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
        int orderId, IReadOnlyCollection<ReservationItem> items, CancellationToken cancellationToken) =>
        ReserveAsync(new StockReservation(orderId, items), cancellationToken);

    public async Task<StockReservation> ReserveForCheckoutAsync(
        int orderId, Guid checkoutId, IReadOnlyCollection<ReservationItem> items, CancellationToken cancellationToken) =>
        (await ReserveAsync(new StockReservation(orderId, items, checkoutId), cancellationToken)).Reservation;

    public async Task<StockReservation> RejectForCheckoutAsync(
        int orderId, Guid checkoutId, string reason, CancellationToken cancellationToken) =>
        (await ReserveAsync(StockReservation.RecordRejection(orderId, checkoutId, "InvalidReservation", reason),
            cancellationToken)).Reservation;

    private Task<(StockReservation Reservation, bool Created)> ReserveAsync(
        StockReservation requested, CancellationToken cancellationToken)
    {
        return InTransactionAsync(async () =>
        {
            var existing = await FindReservationForUpdateAsync(requested.OrderId, requested.CheckoutId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Status == ReservationStatus.Rejected || (existing.IsReleased && requested.CheckoutId.HasValue))
                    return (existing, false);
                if (existing.IsReleased && existing.Items.Count == 0)
                    throw new InventoryConflictException("This order was released before stock was reserved.");
                if (!existing.Matches(requested))
                    throw new InventoryConflictException("This order already has a reservation with different items.",
                        code: "ReservationItemsMismatch");
                return (existing, false);
            }

            var allocations = new List<(InventoryItem Stock, int Quantity)>();
            foreach (var line in requested.Items.OrderBy(x => x.CatalogItemId))
            {
                var stock = await repository.GetStockForUpdateAsync(line.CatalogItemId, cancellationToken);
                if (stock is not null && stock.AvailableQuantity >= line.Quantity)
                {
                    allocations.Add((stock, line.Quantity));
                    continue;
                }

                // An identical concurrent request may have reserved these units since the first read.
                if (await repository.GetReservationAsync(requested.OrderId, cancellationToken) is not null)
                    throw new InventoryConcurrencyException("The order was reserved concurrently; reload its outcome.");

                var reason = stock is null ? $"Stock is not initialized for product {line.CatalogItemId}."
                    : "There is not enough available stock for this reservation.";
                var available = stock?.AvailableQuantity ?? 0;
                if (requested.CheckoutId is not Guid checkoutId)
                {
                    if (stock is null)
                        throw new KeyNotFoundException(reason);

                    throw new InventoryConflictException(reason,
                        new StockShortage(line.CatalogItemId, line.Quantity, available));
                }

                var rejected = StockReservation.RecordRejection(requested.OrderId, checkoutId,
                    stock is null ? "StockNotInitialized" : "InsufficientStock", reason,
                    line.CatalogItemId, line.Quantity, available);
                repository.AddReservation(rejected);
                return (rejected, true);
            }

            foreach (var (stock, quantity) in allocations)
                stock.Reserve(quantity);

            repository.AddReservation(requested);
            return (requested, true);
        }, cancellationToken);
    }

    public Task<StockReservation> ConfirmAsync(int orderId, CancellationToken cancellationToken, Guid? checkoutId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        return InTransactionAsync(async () =>
        {
            var reservation = await FindReservationForUpdateAsync(orderId, checkoutId, cancellationToken)
                ?? throw new KeyNotFoundException("Reservation not found.");
            ApplyStockRule(reservation.Confirm);
            return reservation;
        }, cancellationToken);
    }

    public Task<StockReservation> ReleaseAsync(int orderId, CancellationToken cancellationToken) =>
        ReleaseAsync(orderId, checkoutId: null, cancellationToken);

    public Task<StockReservation> ReleaseForCheckoutAsync(int orderId, Guid checkoutId, CancellationToken cancellationToken) =>
        ReleaseAsync(orderId, checkoutId, cancellationToken);

    private Task<StockReservation> ReleaseAsync(int orderId, Guid? checkoutId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        return InTransactionAsync(async () =>
        {
            var reservation = await FindReservationForUpdateAsync(orderId, checkoutId, cancellationToken);
            if (reservation is null)
            {
                if (checkoutId is not Guid id)
                    throw new KeyNotFoundException("Reservation not found.");

                reservation = StockReservation.RecordEarlyRelease(orderId, id);
                repository.AddReservation(reservation);
                return reservation;
            }
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

    private async Task<StockReservation?> FindReservationForUpdateAsync(
        int orderId, Guid? checkoutId, CancellationToken cancellationToken)
    {
        var reservation = await repository.GetReservationForUpdateAsync(orderId, cancellationToken);
        if ((reservation is not null && reservation.CheckoutId != checkoutId)
            || (reservation is null && checkoutId is Guid id
                && await repository.HasReservationForAnotherOrderAsync(id, orderId, cancellationToken)))
        {
            throw new InventoryConflictException("The order and checkout do not match the saved reservation.",
                code: "CheckoutBindingMismatch");
        }
        return reservation;
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
        throw new InventoryConflictException("Inventory changed concurrently. Please retry the operation.", retryable: true);
    }

    private static void ApplyStockRule(Action change)
    {
        try { change(); }
        catch (InvalidOperationException error) { throw new InventoryConflictException(error.Message); }
    }
}
