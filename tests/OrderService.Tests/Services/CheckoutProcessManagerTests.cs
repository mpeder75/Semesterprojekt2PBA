using Ardalis.Specification;
using NSubstitute;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Messages;
using OrderService.api.Services;
using OrderService.Tests.Builders;

namespace OrderService.Tests.Services;

public class CheckoutProcessManagerTests
{
    private readonly IRepository<CheckoutProcess> _checkoutRepo = Substitute.For<IRepository<CheckoutProcess>>();
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IBasketClient _basket = Substitute.For<IBasketClient>();
    private readonly ICatalogClient _catalog = Substitute.For<ICatalogClient>();
    private readonly IInventoryClient _inventory = Substitute.For<IInventoryClient>();
    private readonly CheckoutProcessManager _manager;

    // Den ordre manageren opretter, så vi kan finde den igen og kontrollere dens status
    private Order? _savedOrder;

    public CheckoutProcessManagerTests()
    {
        _manager = new CheckoutProcessManager(_checkoutRepo, _basket, _catalog, _inventory, _orderRepo);

        // Når manageren gemmer en ordre, husker vi den ...
        _orderRepo.AddAsync(Arg.Do<Order>(o => _savedOrder = o), Arg.Any<CancellationToken>());

        // ... og når den slår ordren op igen, får den den samme ordre tilbage
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _savedOrder);
    }

    // ---------- hjælpere ----------

    private static CheckoutProcess NewCheckout() =>
        new(Guid.NewGuid(), basketId: 1, buyerId: "test@test.dk", new AddressBuilder().WithDefaultValues());

    private void CheckoutIs(CheckoutProcess? checkout)
    {
        _checkoutRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<CheckoutProcess>>(), Arg.Any<CancellationToken>())
            .Returns(checkout);
    }

    private void BasketReturnsTwoLines() =>
        _basket.PrepareBasketForCheckoutAsync(Arg.Any<int>(), Arg.Any<string>())
            .Returns(new PrepareBasketResult
            {
                IsSuccess = true,
                Items = new List<BasketLineDto>
                {
                    new() { CatalogItemId = 1, Quantity = 2 },
                    new() { CatalogItemId = 2, Quantity = 1 }
                }
            });

    private void CatalogReturnsBothItems() =>
        _catalog.GetCatalogItemsByIdsAsync(Arg.Any<List<int>>())
            .Returns(new GetCatalogItemsResult
            {
                IsSuccess = true,
                Items = new List<CatalogItemDto>
                {
                    new() { CatalogItemId = 1, Name = "Item 1", Price = 10m, PictureUri = "http://x/1.jpg" },
                    new() { CatalogItemId = 2, Name = "Item 2", Price = 20m, PictureUri = "http://x/2.jpg" }
                }
            });

    private void InventoryAcceptsReserveAndConfirm()
    {
        _inventory.ReserveStock(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<List<StockLineDto>>())
            .Returns(new ReserveStockResult { IsSuccess = true });
        _inventory.ConfirmStockReservation(Arg.Any<Guid>())
            .Returns(new ConfirmStockReservationResult { IsSuccess = true });
    }

    private void BasketCompleteSucceeds() =>
        _basket.CompleteBasketCheckoutAsync(Arg.Any<int>(), Arg.Any<Guid>())
            .Returns(new CompleteBasketCheckoutResult { IsSuccess = true });

    private void EverythingSucceeds()
    {
        BasketReturnsTwoLines();
        CatalogReturnsBothItems();
        InventoryAcceptsReserveAndConfirm();
        BasketCompleteSucceeds();
    }

    // ---------- lykkelig vej ----------

    [Fact]
    public async Task HappyPath_EndsInCompleted()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        EverythingSucceeds();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.Completed, checkout.State);
    }

    [Fact]
    public async Task HappyPath_SavesCatalogSnapshotOnTheLines()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        EverythingSucceeds();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        var line1 = Assert.Single(checkout.Lines, l => l.CatalogItemId == 1);
        Assert.Equal("Item 1", line1.ProductName);
        Assert.Equal(10m, line1.UnitPrice);
        Assert.Equal("http://x/1.jpg", line1.PictureUri);
        Assert.Equal(2, checkout.Lines.Count);
    }

    [Fact]
    public async Task HappyPath_CreatesOrderWithItemsAndConfirmsIt()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        EverythingSucceeds();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.NotNull(_savedOrder);
        Assert.Equal(OrderStatus.Confirmed, _savedOrder!.Status);
        Assert.Equal(checkout.CheckoutId, _savedOrder.CheckoutId);
        Assert.Equal(2, _savedOrder.OrderItems.Count);
        Assert.Equal(40m, _savedOrder.Total()); // 10*2 + 20*1
    }

    [Fact]
    public async Task HappyPath_ReservesStockWithCheckoutIdAndAllLines()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        EverythingSucceeds();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        await _inventory.Received(1).ReserveStock(
            checkout.CheckoutId,
            Arg.Any<int>(),
            Arg.Is<List<StockLineDto>>(l => l.Count == 2));
        await _inventory.Received(1).ConfirmStockReservation(checkout.CheckoutId);
        await _basket.Received(1).CompleteBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);
    }

    // ---------- fejl stopper flowet ----------

    [Fact]
    public async Task BasketRejects_StaysAwaitingBasketAndNothingElseIsCalled()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        _basket.PrepareBasketForCheckoutAsync(Arg.Any<int>(), Arg.Any<string>())
            .Returns(new PrepareBasketResult { IsSuccess = false, Reason = "Basket is empty" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.AwaitingBasket, checkout.State);
        await _catalog.DidNotReceiveWithAnyArgs().GetCatalogItemsByIdsAsync(default!);
        await _inventory.DidNotReceiveWithAnyArgs().ReserveStock(default, default, default!);
    }

    [Fact]
    public async Task CatalogRejects_StaysAwaitingCatalog()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        BasketReturnsTwoLines();
        _catalog.GetCatalogItemsByIdsAsync(Arg.Any<List<int>>())
            .Returns(new GetCatalogItemsResult { IsSuccess = false, Reason = "Catalog down" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.AwaitingCatalog, checkout.State);
        await _inventory.DidNotReceiveWithAnyArgs().ReserveStock(default, default, default!);
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task CatalogMissingAnItem_StaysAwaitingCatalog()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        BasketReturnsTwoLines();
        // Catalog kender kun vare 1, selvom kurven indeholder 1 og 2
        _catalog.GetCatalogItemsByIdsAsync(Arg.Any<List<int>>())
            .Returns(new GetCatalogItemsResult
            {
                IsSuccess = true,
                Items = new List<CatalogItemDto>
                {
                    new() { CatalogItemId = 1, Name = "Item 1", Price = 10m, PictureUri = "http://x/1.jpg" }
                }
            });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.AwaitingCatalog, checkout.State);
        await _inventory.DidNotReceiveWithAnyArgs().ReserveStock(default, default, default!);
    }

    [Fact]
    public async Task InventoryRejectsReservation_StaysAwaitingStockReservationAndOrderStaysPending()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        BasketReturnsTwoLines();
        CatalogReturnsBothItems();
        _inventory.ReserveStock(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<List<StockLineDto>>())
            .Returns(new ReserveStockResult { IsSuccess = false, Reason = "Not enough stock" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.AwaitingStockReservation, checkout.State);
        Assert.Equal(OrderStatus.PendingInventory, _savedOrder!.Status);
        await _inventory.DidNotReceiveWithAnyArgs().ConfirmStockReservation(default);
    }

    [Fact]
    public async Task InventoryRejectsConfirmation_StaysAwaitingStockConfirmationAndOrderIsNotConfirmed()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        BasketReturnsTwoLines();
        CatalogReturnsBothItems();
        _inventory.ReserveStock(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<List<StockLineDto>>())
            .Returns(new ReserveStockResult { IsSuccess = true });
        _inventory.ConfirmStockReservation(Arg.Any<Guid>())
            .Returns(new ConfirmStockReservationResult { IsSuccess = false, Reason = "Reservation expired" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.AwaitingStockConfirmation, checkout.State);
        Assert.Equal(OrderStatus.PendingInventory, _savedOrder!.Status);
        await _basket.DidNotReceiveWithAnyArgs().CompleteBasketCheckoutAsync(default, default);
    }

    [Fact]
    public async Task BasketCleanupFails_OrderIsStillConfirmedAndCheckoutWaitsForCleanup()
    {
        var checkout = NewCheckout();
        CheckoutIs(checkout);
        BasketReturnsTwoLines();
        CatalogReturnsBothItems();
        InventoryAcceptsReserveAndConfirm();
        _basket.CompleteBasketCheckoutAsync(Arg.Any<int>(), Arg.Any<Guid>())
            .Returns(new CompleteBasketCheckoutResult { IsSuccess = false, Reason = "Basket service down" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.OrderConfirmedCleanupPending, checkout.State);
        Assert.Equal(OrderStatus.Confirmed, _savedOrder!.Status);
    }

    // ---------- kanttilfælde ----------

    [Fact]
    public async Task UnknownCheckout_DoesNothing()
    {
        CheckoutIs(null);

        await _manager.AdvanceOrderAsync(Guid.NewGuid());

        await _basket.DidNotReceiveWithAnyArgs().PrepareBasketForCheckoutAsync(default, default!);
        await _checkoutRepo.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task AlreadyCompletedCheckout_DoesNotCallAnyone()
    {
        var checkout = NewCheckout();
        checkout.MoveTo(CheckoutState.Completed);
        CheckoutIs(checkout);

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.Completed, checkout.State);
        await _basket.DidNotReceiveWithAnyArgs().PrepareBasketForCheckoutAsync(default, default!);
        await _inventory.DidNotReceiveWithAnyArgs().ReserveStock(default, default, default!);
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }
}
