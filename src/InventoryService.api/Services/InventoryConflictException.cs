namespace InventoryService.api.Services;

public class InventoryConflictException(string message) : Exception(message);
