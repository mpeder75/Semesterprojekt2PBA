namespace InventoryService.api.Entities;

public class InventoryItem
{
    public int CatalogItemId { get; private set; }
    public int Quantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
    public int AvailableQuantity => Quantity - ReservedQuantity;

    public InventoryItem(int catalogItemId, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(catalogItemId);
        CatalogItemId = catalogItemId;
        SetQuantity(quantity);
    }

    public void SetQuantity(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        if (quantity < ReservedQuantity)
        {
            throw new InvalidOperationException("On-hand stock cannot be lower than reserved stock.");
        }
        Quantity = quantity;
    }

    public void AdjustQuantity(int change)
    {
        if (change == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(change), "A stock adjustment must be non-zero.");
        }

        var newQuantity = checked(Quantity + change);
        if (newQuantity < ReservedQuantity)
        {
            throw new InvalidOperationException("There is not enough stock for this adjustment.");
        }

        Quantity = newQuantity;
    }

    public void Reserve(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (quantity > AvailableQuantity)
        {
            throw new InvalidOperationException("There is not enough available stock for this reservation.");
        }
        ReservedQuantity += quantity;
    }

    public void Release(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (quantity > ReservedQuantity)
        {
            throw new InvalidOperationException("Cannot release more units than are reserved.");
        }
        ReservedQuantity -= quantity;
    }
}
