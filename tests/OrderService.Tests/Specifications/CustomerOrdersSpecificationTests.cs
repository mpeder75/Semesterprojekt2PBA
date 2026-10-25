using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Specifications;

namespace OrderService.Tests.Specifications;

public class CustomerOrdersSpecificationTests
{
    private readonly Address _shipToAddress = new Address("Street", "City", "OH", "US", "11111");

    [Fact]
    public void ReturnsOnlyOrdersForTheBuyer()
    {
        var spec = new CustomerOrdersSpecification("buyer-1");

        var result = spec.Evaluate(GetTestCollection()).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, o => Assert.Equal("buyer-1", o.BuyerId));
    }

    [Fact]
    public void ReturnsOrdersWithTheirItems()
    {
        var spec = new CustomerOrdersSpecification("buyer-1");

        var result = spec.Evaluate(GetTestCollection()).ToList();

        Assert.Single(result[0].OrderItems);
        Assert.Equal(2, result[1].OrderItems.Count);
    }

    [Fact]
    public void ReturnsEmptyForUnknownBuyer()
    {
        var spec = new CustomerOrdersSpecification("ukendt");

        var result = spec.Evaluate(GetTestCollection()).ToList();

        Assert.Empty(result);
    }

    private List<Order> GetTestCollection()
    {
        return new List<Order>
        {
            new Order("buyer-1", _shipToAddress, new List<OrderItem>
            {
                new OrderItem(new CatalogItemOrdered(1, "Product1", "testurl"), 10.50m, 1)
            }),
            new Order("buyer-1", _shipToAddress, new List<OrderItem>
            {
                new OrderItem(new CatalogItemOrdered(2, "Product2", "testurl"), 15.50m, 2),
                new OrderItem(new CatalogItemOrdered(3, "Product3", "testurl"), 20.50m, 1)
            }),
            new Order("buyer-2", _shipToAddress, new List<OrderItem>
            {
                new OrderItem(new CatalogItemOrdered(4, "Product4", "testurl"), 1m, 1)
            })
        };
    }
}
