namespace InventoryService.api.Services;

public class InventoryConflictException(string message, StockShortage? shortage = null, bool retryable = false,
    string? code = null)
    : Exception(message)
{
    public StockShortage? Shortage { get; } = shortage;
    public bool Retryable { get; } = retryable;
    public string? Code { get; } = code;
}

public sealed record StockShortage(int CatalogItemId, int RequestedQuantity, int AvailableQuantity);
