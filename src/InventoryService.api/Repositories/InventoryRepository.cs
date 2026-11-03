using System.Data;
using InventoryService.api.Entities;
using InventoryService.api.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryService.api.Repositories;

public class InventoryRepository(InventoryDbContext database) : IInventoryRepository
{
    public Task<InventoryItem?> GetStockAsync(int catalogItemId, CancellationToken cancellationToken) =>
        database.InventoryItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CatalogItemId == catalogItemId, cancellationToken);

    public Task<InventoryItem?> GetStockForUpdateAsync(int catalogItemId, CancellationToken cancellationToken) =>
        database.InventoryItems.FromSqlInterpolated($"""
            SELECT * FROM [InventoryItems] WITH (UPDLOCK, HOLDLOCK) WHERE [CatalogItemId] = {catalogItemId}
            """).SingleOrDefaultAsync(cancellationToken);

    public Task<StockReservation?> GetReservationAsync(int orderId, CancellationToken cancellationToken) =>
        database.Reservations.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    public Task<StockReservation?> GetReservationForUpdateAsync(int orderId, CancellationToken cancellationToken) =>
        database.Reservations.FromSqlInterpolated($"""
            SELECT * FROM [Reservations] WITH (UPDLOCK, HOLDLOCK) WHERE [OrderId] = {orderId}
            """).Include(x => x.Items).SingleOrDefaultAsync(cancellationToken);

    public void AddStock(InventoryItem item) => database.InventoryItems.Add(item);

    public void RemoveStock(InventoryItem item) => database.InventoryItems.Remove(item);

    public void AddReservation(StockReservation reservation) => database.Reservations.Add(reservation);

    public async Task<IInventoryTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        return new InventoryTransaction(database, transaction);
    }

    private sealed class InventoryTransaction(InventoryDbContext database, IDbContextTransaction transaction)
        : IInventoryTransaction
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await database.SaveChangesAsync(cancellationToken);
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
