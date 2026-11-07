namespace OrderService.api.Entities.OrderAggregate;

public class CheckoutLine : BaseEntity
{
    public int CatalogItemId { get; private set; }
    public int Quantity { get; private set; }

    // Ef constructor
    private CheckoutLine() { }

    public CheckoutLine(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }
}
