using Ardalis.Specification;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using OrderService.api.Endpoints.OrderEndpoints;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.Tests.Builders;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Endpoints;

public class OrderGetByIdEndpointTests
{
    private readonly IReadRepository<Order> _repository = Substitute.For<IReadRepository<Order>>();
    private readonly OrderGetByIdEndpoint _endpoint = new();

    [Fact]
    public async Task UnknownOrder_ReturnsNotFound()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _endpoint.HandleAsync(new GetByIdOrderRequest(99), _repository);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task ExistingOrder_ReturnsOkWithMappedOrder()
    {
        var builder = new OrderBuilder();
        var order = builder.WithDefaultValues().WithId(5);
        _repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _endpoint.HandleAsync(new GetByIdOrderRequest(5), _repository);

        var ok = Assert.IsType<Ok<GetByIdOrderResponse>>(result);
        var dto = ok.Value!.Order;
        Assert.Equal(5, dto.Id);
        Assert.Equal(builder.TestUnitPrice * builder.TestUnits, dto.Total);
        Assert.Equal("123 Main St.", dto.Address.Street);
        var item = Assert.Single(dto.OrderItems);
        Assert.Equal(builder.TestCatalogItemId, item.ProductId);
        Assert.Equal(builder.TestProductName, item.ProductName);
        Assert.Equal(builder.TestUnits, item.Units);
    }
}
