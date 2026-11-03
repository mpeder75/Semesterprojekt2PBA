namespace InventoryService.api.Entities;

public class ReservationItem
{
    public int CatalogItemId { get; private set; }
    public int Quantity { get; private set; }

    public ReservationItem(int catalogItemId, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(catalogItemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }
}
