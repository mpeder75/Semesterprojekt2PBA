namespace InventoryService.api.Interfaces;

public interface IInventoryTransaction : IAsyncDisposable
{
    // Persist all pending changes atomically. Disposing without committing abandons the operation.
    Task CommitAsync(CancellationToken cancellationToken);
}
