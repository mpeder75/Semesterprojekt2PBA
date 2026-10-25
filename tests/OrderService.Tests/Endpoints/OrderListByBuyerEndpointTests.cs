using Ardalis.Specification;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using OrderService.api.Endpoints.OrderEndpoints;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.Tests.Builders;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Endpoints;

public class OrderListByBuyerEndpointTests
{
    private readonly IReadRepository<Order> _repository = Substitute.For<IReadRepository<Order>>();
    private readonly OrderListByBuyerEndpoint _endpoint = new();

    [Fact]
    public async Task BuyerWithoutOrders_ReturnsOkWithEmptyList()
    {
        _repository.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order>());

        var result = await _endpoint.HandleAsync(new ListOrdersByBuyerRequest("ingen@test.dk"), _repository);

        var ok = Assert.IsType<Ok<ListOrdersByBuyerResponse>>(result);
        Assert.Empty(ok.Value!.Orders);
    }

    [Fact]
    public async Task BuyerWithOrders_ReturnsSummaryForEachOrder()
    {
        var builder = new OrderBuilder();
        var orders = new List<Order>
        {
            builder.WithDefaultValues().WithId(1),
            builder.WithDefaultValues().WithId(2)
        };
        _repository.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(orders);

        var result = await _endpoint.HandleAsync(new ListOrdersByBuyerRequest(builder.TestBuyerId), _repository);

        var ok = Assert.IsType<Ok<ListOrdersByBuyerResponse>>(result);
        Assert.Equal(2, ok.Value!.Orders.Count);
        Assert.Equal(new[] { 1, 2 }, ok.Value.Orders.Select(o => o.Id));
        Assert.All(ok.Value.Orders, o => Assert.Equal(builder.TestUnitPrice * builder.TestUnits, o.Total));
    }
}
