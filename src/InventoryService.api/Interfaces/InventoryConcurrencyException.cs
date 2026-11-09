namespace InventoryService.api.Interfaces;

public sealed class InventoryConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
