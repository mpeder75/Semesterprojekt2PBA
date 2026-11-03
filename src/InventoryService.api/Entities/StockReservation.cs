namespace InventoryService.api.Entities;

public class StockReservation
{
    private readonly List<ReservationItem> _items = new();
    public int OrderId { get; private set; }
    public bool IsReleased { get; private set; }
    public IReadOnlyCollection<ReservationItem> Items => _items;

    private StockReservation() { }

    public StockReservation(int orderId, IReadOnlyCollection<ReservationItem> items)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        if (items.Count == 0 || items.Select(x => x.CatalogItemId).Distinct().Count() != items.Count)
            throw new ArgumentException("A reservation needs at least one item, with each product listed once.");
        OrderId = orderId;
        _items.AddRange(items.OrderBy(x => x.CatalogItemId));
    }

    public bool Matches(StockReservation other) => Items.OrderBy(x => x.CatalogItemId)
        .Select(x => (x.CatalogItemId, x.Quantity))
        .SequenceEqual(other.Items.OrderBy(x => x.CatalogItemId).Select(x => (x.CatalogItemId, x.Quantity)));

    public void MarkReleased() => IsReleased = true;
}
