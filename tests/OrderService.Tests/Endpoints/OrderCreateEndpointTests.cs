using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using OrderService.api.Endpoints.OrderEndpoints;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;

namespace OrderService.Tests.Endpoints;

public class OrderCreateEndpointTests
{
    private readonly IRepository<Order> _repository = Substitute.For<IRepository<Order>>();
    private readonly OrderCreateEndpoint _endpoint = new();

    private static CreateOrderRequest ValidRequest() => new()
    {
        BuyerId = "test@test.dk",
        ShipToAddress = new AddressDto { Street = "Vej 1", City = "By", State = "DK", Country = "Danmark", ZipCode = "1000" },
        Items = new List<CreateOrderItemRequest>
        {
            new() { CatalogItemId = 1, ProductName = "Test", PictureUrl = "http://localhost/1.png", UnitPrice = 10, Units = 2 }
        }
    };

    [Fact]
    public async Task ValidRequest_ReturnsCreatedAndSavesOrder()
    {
        var result = await _endpoint.HandleAsync(ValidRequest(), _repository);

        Assert.IsType<Created<CreateOrderResponse>>(result);
        await _repository.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidRequest_MapsRequestToOrder()
    {
        Order? saved = null;
        await _repository.AddAsync(Arg.Do<Order>(o => saved = o), Arg.Any<CancellationToken>());

        await _endpoint.HandleAsync(ValidRequest(), _repository);

        Assert.NotNull(saved);
        Assert.Equal("test@test.dk", saved!.BuyerId);
        Assert.Equal("Vej 1", saved.ShipToAddress.Street);
        Assert.Single(saved.OrderItems);
        Assert.Equal(20m, saved.Total());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyBuyerId_ReturnsBadRequestAndSavesNothing(string buyerId)
    {
        var request = ValidRequest();
        request.BuyerId = buyerId;

        var result = await _endpoint.HandleAsync(request, _repository);

        Assert.IsType<BadRequest<string>>(result);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoItems_ReturnsBadRequestAndSavesNothing()
    {
        var request = ValidRequest();
        request.Items = new List<CreateOrderItemRequest>();

        var result = await _endpoint.HandleAsync(request, _repository);

        Assert.IsType<BadRequest<string>>(result);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
