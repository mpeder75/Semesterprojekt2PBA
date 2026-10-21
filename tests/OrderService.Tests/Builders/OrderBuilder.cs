using OrderService.api.Entities.OrderAggregate;

namespace OrderService.Tests.Builders;

public class OrderBuilder
{
    public string TestBuyerId => "12345";
    public int TestCatalogItemId => 234;
    public string TestProductName => "Test Product Name";
    public string TestPictureUri => "http://test.com/image.jpg";
    public decimal TestUnitPrice = 1.23m;
    public int TestUnits = 3;
    public CatalogItemOrdered TestCatalogItemOrdered { get; }

    public OrderBuilder()
    {
        TestCatalogItemOrdered = new CatalogItemOrdered(TestCatalogItemId, TestProductName, TestPictureUri);
    }

    public Order WithDefaultValues()
    {
        var orderItem = new OrderItem(TestCatalogItemOrdered, TestUnitPrice, TestUnits);
        return new Order(TestBuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { orderItem });
    }

    public Order WithNoItems()
    {
        return new Order(TestBuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem>());
    }

    public Order WithItems(List<OrderItem> items)
    {
        return new Order(TestBuyerId, new AddressBuilder().WithDefaultValues(), items);
    }
}
