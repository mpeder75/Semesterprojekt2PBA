using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Specifications;
using OrderService.Tests.Builders;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Specifications;

public class OrderWithItemsByIdSpecTests
{
    [Fact]
    public void ReturnsTheOrderWithMatchingId()
    {
        var orders = new List<Order>
        {
            new OrderBuilder().WithDefaultValues().WithId(1),
            new OrderBuilder().WithDefaultValues().WithId(2)
        };

        var result = new OrderWithItemsByIdSpec(2).Evaluate(orders).ToList();

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public void ReturnsNothingWhenIdDoesNotExist()
    {
        var orders = new List<Order> { new OrderBuilder().WithDefaultValues().WithId(1) };

        var result = new OrderWithItemsByIdSpec(99).Evaluate(orders).ToList();

        Assert.Empty(result);
    }
}
