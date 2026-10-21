using OrderService.api.Entities.OrderAggregate;
using OrderService.Tests.Builders;

namespace OrderService.Tests.Entities;

public class OrderTotalTests
{
    private readonly decimal _testUnitPrice = 42m;

    [Fact]
    public void IsZeroForNewOrder()
    {
        var order = new OrderBuilder().WithNoItems();

        Assert.Equal(0, order.Total());
    }

    [Fact]
    public void IsCorrectGiven1Item()
    {
        var builder = new OrderBuilder();
        var items = new List<OrderItem>
        {
            new OrderItem(builder.TestCatalogItemOrdered, _testUnitPrice, 1)
        };

        var order = builder.WithItems(items);

        Assert.Equal(_testUnitPrice, order.Total());
    }

    [Fact]
    public void IsCorrectGivenUnitsAboveOne()
    {
        var builder = new OrderBuilder();
        var order = builder.WithDefaultValues();

        Assert.Equal(builder.TestUnitPrice * builder.TestUnits, order.Total());
    }

    [Fact]
    public void SumsAllItems()
    {
        var builder = new OrderBuilder();
        var items = new List<OrderItem>
        {
            new OrderItem(builder.TestCatalogItemOrdered, 10m, 2),
            new OrderItem(builder.TestCatalogItemOrdered, 5m, 1)
        };

        var order = builder.WithItems(items);

        Assert.Equal(25m, order.Total());
    }
}
