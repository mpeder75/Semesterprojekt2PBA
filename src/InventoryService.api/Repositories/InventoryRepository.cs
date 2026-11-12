using System.Data;
using InventoryService.api.Entities;
using InventoryService.api.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryService.api.Repositories;

public class InventoryRepository(InventoryDbContext database) : IInventoryRepository
{
    public Task<InventoryItem?> GetStockAsync(int catalogItemId, CancellationToken cancellationToken) =>
        database.InventoryItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CatalogItemId == catalogItemId, cancellationToken);

    public Task<InventoryItem?> GetStockForUpdateAsync(int catalogItemId, CancellationToken cancellationToken) =>
        database.InventoryItems.SingleOrDefaultAsync(x => x.CatalogItemId == catalogItemId, cancellationToken);

    public Task<StockReservation?> GetReservationAsync(int orderId, CancellationToken cancellationToken) =>
        database.Reservations.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    public Task<StockReservation?> GetReservationForUpdateAsync(int orderId, CancellationToken cancellationToken) =>
        database.Reservations.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    public Task<bool> HasReservationForAnotherOrderAsync(Guid checkoutId, int orderId, CancellationToken cancellationToken) =>
        database.Reservations.AnyAsync(x => x.CheckoutId == checkoutId && x.OrderId != orderId, cancellationToken);

    public void AddStock(InventoryItem item) => database.InventoryItems.Add(item);

    public void RemoveStock(InventoryItem item) => database.InventoryItems.Remove(item);

    public void AddReservation(StockReservation reservation) => database.Reservations.Add(reservation);

    public async Task<IInventoryTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return new InventoryTransaction(database, transaction);
    }

    private sealed class InventoryTransaction(InventoryDbContext database, IDbContextTransaction transaction)
        : IInventoryTransaction
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException error)
            {
                throw new InventoryConcurrencyException("Inventory changed while the operation was being saved.", error);
            }
            catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // A concurrent request inserted the same stock/order ID after our initial read.
                throw new InventoryConcurrencyException("Another request created this stock record or reservation.", error);
            }

            // A commit failure has an uncertain outcome. Do not translate it into a retryable conflict.
            await transaction.CommitAsync(cancellationToken);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally
            {
                // Discard pending entity changes as well as rolling back the SQL transaction.
                if (!_committed) database.ChangeTracker.Clear();
            }
        }
    }
}
