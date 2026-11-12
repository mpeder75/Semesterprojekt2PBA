namespace InventoryService.api.Services;

public class InventoryConflictException(string message, StockShortage? shortage = null, bool retryable = false)
    : Exception(message)
{
    public StockShortage? Shortage { get; } = shortage;
    public bool Retryable { get; } = retryable;
}

public sealed record StockShortage(int CatalogItemId, int RequestedQuantity, int AvailableQuantity);
