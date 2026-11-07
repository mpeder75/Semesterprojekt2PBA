namespace OrderService.api.Entities.OrderAggregate;

public class CheckoutLine : BaseEntity
{
    public int CatalogItemId { get; private set; }
    public int Quantity { get; private set; }
    public string? ProductName { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public string? PictureUri { get; private set; }

    // Ef constructor
    private CheckoutLine() { }

    public CheckoutLine(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public void SetCatalogSnapshot(string productName, decimal unitPrice, string pictureUri)
    {
        ProductName = productName;
        UnitPrice = unitPrice;
        PictureUri = pictureUri;
    }
}
