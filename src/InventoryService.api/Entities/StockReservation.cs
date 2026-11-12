namespace InventoryService.api.Entities;

public class StockReservation
{
    private readonly List<ReservationItem> _items = new();
    public int OrderId { get; private set; }
    public Guid? CheckoutId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public bool IsReleased => Status == ReservationStatus.Released;
    public bool IsConfirmed => Status == ReservationStatus.Confirmed;
    public string? RejectionCode { get; private set; }
    public string? RejectionReason { get; private set; }
    public int? RejectionProductId { get; private set; }
    public int? RejectionRequestedQuantity { get; private set; }
    public int? RejectionAvailableQuantity { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
    public IReadOnlyCollection<ReservationItem> Items => _items;

    private StockReservation() { }

    public StockReservation(int orderId, IReadOnlyCollection<ReservationItem> items, Guid? checkoutId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        if (items.Count == 0 || items.Select(x => x.CatalogItemId).Distinct().Count() != items.Count)
            throw new ArgumentException("A reservation needs at least one item, with each product listed once.");
        OrderId = orderId;
        CheckoutId = checkoutId;
        _items.AddRange(items.OrderBy(x => x.CatalogItemId));
    }

    public bool Matches(StockReservation other) => Items.OrderBy(x => x.CatalogItemId)
        .Select(x => (x.CatalogItemId, x.Quantity))
        .SequenceEqual(other.Items.OrderBy(x => x.CatalogItemId).Select(x => (x.CatalogItemId, x.Quantity)));

    public static StockReservation RecordEarlyRelease(int orderId, Guid checkoutId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        return new StockReservation { OrderId = orderId, CheckoutId = checkoutId, Status = ReservationStatus.Released };
    }

    public static StockReservation RecordRejection(int orderId, Guid checkoutId, string code, string reason,
        int? productId = null, int? requestedQuantity = null, int? availableQuantity = null) =>
        new()
        {
            OrderId = orderId, CheckoutId = checkoutId, Status = ReservationStatus.Rejected,
            RejectionCode = code, RejectionReason = reason, RejectionProductId = productId,
            RejectionRequestedQuantity = requestedQuantity, RejectionAvailableQuantity = availableQuantity
        };

    public void Confirm()
    {
        if (Status is not (ReservationStatus.Reserved or ReservationStatus.Confirmed))
            throw new InvalidOperationException("Only an active reservation can be confirmed.");

        Status = ReservationStatus.Confirmed;
    }

    public void MarkReleased()
    {
        Status = ReservationStatus.Released;
    }
}

public enum ReservationStatus
{
    Reserved = 0,
    Confirmed = 1,
    Released = 2,
    Rejected = 3
}
