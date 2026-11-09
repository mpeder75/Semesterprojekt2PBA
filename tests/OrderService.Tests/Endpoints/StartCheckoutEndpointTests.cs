using Ardalis.Specification;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using OrderService.api.Endpoints.OrderEndpoints;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Services;
using OrderService.api.Specifications;

namespace OrderService.Tests.Endpoints;

public class StartCheckoutEndpointTests
{
    private readonly IRepository<CheckoutProcess> _checkoutRepo = Substitute.For<IRepository<CheckoutProcess>>();
    private readonly CheckoutProcessManager _manager;
    private readonly StartCheckoutEndpoint _endpoint = new();

    public StartCheckoutEndpointTests()
    {
        _manager = new CheckoutProcessManager(
            _checkoutRepo,
            Substitute.For<IRepository<Order>>(),
            Substitute.For<ICheckoutRequestProducer>());
    }

    private static StartCheckoutRequest ValidRequest(int basketId = 1) => new()
    {
        BasketId = basketId,
        BuyerId = "test@test.dk",
        ShipToAddress = new AddressDto { Street = "Vej 1", City = "By", State = "DK", Country = "Danmark", ZipCode = "1000" }
    };

    private static CheckoutProcess CheckoutFor(StartCheckoutRequest request, Guid checkoutId)
    {
        var a = request.ShipToAddress;
        return new CheckoutProcess(checkoutId, request.BasketId, request.BuyerId,
            new Address(a.Street, a.City, a.State, a.Country, a.ZipCode));
    }

    private void CheckoutExistsWithKey(CheckoutProcess checkout) =>
        _checkoutRepo.FirstOrDefaultAsync(Arg.Is<ISpecification<CheckoutProcess>>(s => s is CheckoutProcessByCheckoutIdSpec),
                Arg.Any<CancellationToken>())
            .Returns(checkout);

    private void CheckoutIsActiveOnBasket(CheckoutProcess checkout) =>
        _checkoutRepo.FirstOrDefaultAsync(Arg.Is<ISpecification<CheckoutProcess>>(s => s is ActiveCheckoutByBasketIdSpec),
                Arg.Any<CancellationToken>())
            .Returns(checkout);

    [Fact]
    public async Task SameKeyAndSameRequest_ReturnsAcceptedWithoutCreatingAnother()
    {
        var key = Guid.NewGuid();
        var request = ValidRequest();
        CheckoutExistsWithKey(CheckoutFor(request, key));

        var result = await _endpoint.HandleAsync(request, key, _checkoutRepo, _manager);

        Assert.IsType<Accepted<StartCheckoutResponse>>(result);
        await _checkoutRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task SameKeyAndDifferentBasket_ReturnsConflict()
    {
        var key = Guid.NewGuid();
        CheckoutExistsWithKey(CheckoutFor(ValidRequest(basketId: 1), key));

        var result = await _endpoint.HandleAsync(ValidRequest(basketId: 2), key, _checkoutRepo, _manager);

        Assert.IsType<Conflict<string>>(result);
        await _checkoutRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task SameKeyAndDifferentAddress_ReturnsConflict()
    {
        var key = Guid.NewGuid();
        CheckoutExistsWithKey(CheckoutFor(ValidRequest(), key));
        var changed = ValidRequest();
        changed.ShipToAddress.City = "Andet sted";

        var result = await _endpoint.HandleAsync(changed, key, _checkoutRepo, _manager);

        Assert.IsType<Conflict<string>>(result);
    }

    [Fact]
    public async Task NewKeyOnBasketWithActiveCheckout_ReturnsConflict()
    {
        var request = ValidRequest();
        CheckoutIsActiveOnBasket(CheckoutFor(request, Guid.NewGuid()));

        var result = await _endpoint.HandleAsync(request, Guid.NewGuid(), _checkoutRepo, _manager);

        Assert.IsType<Conflict<string>>(result);
        await _checkoutRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task NewKeyOnFreeBasket_CreatesCheckoutAndReturnsAccepted()
    {
        var key = Guid.NewGuid();

        var result = await _endpoint.HandleAsync(ValidRequest(), key, _checkoutRepo, _manager);

        Assert.IsType<Accepted<StartCheckoutResponse>>(result);
        await _checkoutRepo.Received(1).AddAsync(
            Arg.Is<CheckoutProcess>(c => c.CheckoutId == key && c.BasketId == 1),
            Arg.Any<CancellationToken>());
    }
}
