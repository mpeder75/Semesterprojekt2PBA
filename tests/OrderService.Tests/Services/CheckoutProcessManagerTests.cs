using System.Text.Json;
using Ardalis.Specification;
using NSubstitute;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Messages;
using OrderService.api.Messages.Basket;
using OrderService.api.Messages.Catalog;
using OrderService.api.Messages.Inventory;
using OrderService.api.Services;
using OrderService.Tests.Builders;

namespace OrderService.Tests.Services;

public class CheckoutProcessManagerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<CheckoutProcess> _checkoutRepo = Substitute.For<IRepository<CheckoutProcess>>();
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IBasketClient _basket = Substitute.For<IBasketClient>();
    private readonly ICatalogClient _catalog = Substitute.For<ICatalogClient>();
    private readonly IInventoryClient _inventory = Substitute.For<IInventoryClient>();
    private readonly ICheckoutRequestProducer _producer = Substitute.For<ICheckoutRequestProducer>();
    private readonly CheckoutProcessManager _manager;

    // Den ordre manageren opretter, så vi kan finde den igen og kontrollere dens status
    private Order? _savedOrder;

    public CheckoutProcessManagerTests()
    {
        _manager = new CheckoutProcessManager(_checkoutRepo, _basket, _catalog, _inventory, _orderRepo, _producer);

        // Når manageren gemmer en ordre, husker vi den ...
        _orderRepo.AddAsync(Arg.Do<Order>(o => _savedOrder = o), Arg.Any<CancellationToken>());

        // ... og når den slår ordren op igen, får den den samme ordre tilbage
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _savedOrder);
    }

    // ---------- hjælpere ----------

    /// <summary>En helt ny checkout, der står i AwaitingBasket og endnu ikke har fået svar fra Basket.</summary>
    private static CheckoutProcess FreshCheckout() =>
        new(Guid.NewGuid(), basketId: 1, buyerId: "test@test.dk", new AddressBuilder().WithDefaultValues());

    /// <summary>
    /// En checkout, hvor Basket allerede har svaret: den har to varelinjer og står i AwaitingCatalog.
    /// Bruges af de tests, der starter efter Basket-trinnet.
    /// </summary>
    private static CheckoutProcess CheckoutAfterBasketReply()
    {
        var checkout = FreshCheckout();
        checkout.SetLines(new[] { new CheckoutLine(1, 2), new CheckoutLine(2, 1) });
        checkout.MoveTo(CheckoutState.AwaitingCatalog);
        return checkout;
    }

    private static CheckoutMessageEnvelope ReplyEnvelope(CheckoutProcess checkout, Guid correlationId, string messageType, object payload) =>
        new()
        {
            MessageId = Guid.NewGuid(),
            CheckoutId = checkout.CheckoutId,
            CorrelationId = correlationId,
            MessageType = messageType,
            SentAtUtc = DateTimeOffset.UtcNow,
            Payload = JsonSerializer.SerializeToElement(payload, payload.GetType(), JsonOptions)
        };

    private static BasketPreparedPayload TwoBasketLines() =>
        new()
        {
            Items = new List<BasketLineDto>
            {
                new() { CatalogItemId = 1, Quantity = 2 },
                new() { CatalogItemId = 2, Quantity = 1 }
            }
        };

    private void CheckoutIs(CheckoutProcess? checkout)
    {
        _checkoutRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<CheckoutProcess>>(), Arg.Any<CancellationToken>())
            .Returns(checkout);
    }

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

    private void ReleasesSucceed()
    {
        _inventory.ReleaseStockReservation(Arg.Any<Guid>())
            .Returns(new ReleaseStockReservationResult { IsSuccess = true });
        _basket.ReleaseBasketCheckoutAsync(Arg.Any<int>(), Arg.Any<Guid>())
            .Returns(new ReleaseBasketCheckoutResult { IsSuccess = true });
    }

    private void EverythingSucceeds()
    {
        CatalogReturnsBothItems();
        InventoryAcceptsReserveAndConfirm();
        BasketCompleteSucceeds();
    }

    // ---------- Basket-trinnet: send request, og behandl svaret ----------

    [Fact]
    public async Task AwaitingBasket_SendsPrepareRequestToBasketAndWaits()
    {
        var checkout = FreshCheckout();
        CheckoutIs(checkout);

        Guid? sentCorrelationId = null;
        object? sentPayload = null;
        await _producer.SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Do<Guid>(c => sentCorrelationId = c),
            Arg.Do<object>(p => sentPayload = p),
            Arg.Any<CancellationToken>());

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        await _producer.Received(1).SendAsync(
            CheckoutQueues.Basket, "PrepareBasketForCheckout", checkout.CheckoutId,
            Arg.Any<Guid>(), Arg.Any<object>(), Arg.Any<CancellationToken>());

        // Checkouten venter på svaret og husker, hvilket svar det er
        Assert.Equal(CheckoutState.AwaitingBasket, checkout.State);
        Assert.NotNull(checkout.ExpectedCorrelationId);
        Assert.Equal(sentCorrelationId, checkout.ExpectedCorrelationId);

        var payload = Assert.IsType<PrepareBasketForCheckoutPayload>(sentPayload);
        Assert.Equal(checkout.BasketId, payload.BasketId);
        Assert.Equal(checkout.BuyerId, payload.BuyerId);

        // Det gamle, synkrone kald bruges ikke længere
        await _basket.DidNotReceiveWithAnyArgs().PrepareBasketForCheckoutAsync(default, default!);
    }

    [Fact]
    public async Task BasketPreparedReply_SavesLinesAndContinuesTheFlow()
    {
        var checkout = FreshCheckout();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);
        EverythingSucceeds();

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, correlationId, "BasketPrepared", TwoBasketLines()));

        // Linjerne fra Basket er gemt ...
        Assert.Equal(2, checkout.Lines.Count);
        Assert.Contains(checkout.Lines, l => l.CatalogItemId == 1 && l.Quantity == 2);
        Assert.Contains(checkout.Lines, l => l.CatalogItemId == 2 && l.Quantity == 1);

        // ... og flowet er kørt videre fra AwaitingCatalog (resten er stadig fake-klienter)
        await _catalog.Received(1).GetCatalogItemsByIdsAsync(Arg.Any<List<int>>());
        Assert.Equal(CheckoutState.Completed, checkout.State);
    }

    [Fact]
    public async Task BasketPreparedReply_WhenCatalogRejects_StartsTheFailureTrack()
    {
        var checkout = FreshCheckout();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);
        ReleasesSucceed();
        _catalog.GetCatalogItemsByIdsAsync(Arg.Any<List<int>>())
            .Returns(new GetCatalogItemsResult { IsSuccess = false, Reason = "Catalog down" });

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, correlationId, "BasketPrepared", TwoBasketLines()));

        Assert.Equal(CheckoutState.Failed, checkout.State);
        await _basket.Received(1).ReleaseBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);
    }

    [Fact]
    public async Task Reply_WithWrongCorrelationId_IsIgnored()
    {
        var checkout = FreshCheckout();
        checkout.ExpectReply(Guid.NewGuid());
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, Guid.NewGuid(), "BasketPrepared", TwoBasketLines()));

        Assert.Equal(CheckoutState.AwaitingBasket, checkout.State);
        Assert.Empty(checkout.Lines);
    }

    [Fact]
    public async Task Reply_ForUnknownCheckout_IsIgnored()
    {
        var checkout = FreshCheckout();
        CheckoutIs(null);

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, Guid.NewGuid(), "BasketPrepared", TwoBasketLines()));

        await _checkoutRepo.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task Reply_WithUnknownMessageType_IsIgnored()
    {
        var checkout = FreshCheckout();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, correlationId, "SomethingElse", TwoBasketLines()));

        Assert.Equal(CheckoutState.AwaitingBasket, checkout.State);
        Assert.Empty(checkout.Lines);
        await _checkoutRepo.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    // ---------- lykkelig vej (fra Catalog og frem) ----------

    [Fact]
    public async Task HappyPath_EndsInCompleted()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        EverythingSucceeds();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.Completed, checkout.State);
        Assert.Null(checkout.FailureReason);
    }

    [Fact]
    public async Task HappyPath_SavesCatalogSnapshotOnTheLines()
    {
        var checkout = CheckoutAfterBasketReply();
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
        var checkout = CheckoutAfterBasketReply();
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
        var checkout = CheckoutAfterBasketReply();
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

    [Fact]
    public async Task HappyPath_DoesNotRunTheFailureTrack()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        EverythingSucceeds();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        await _inventory.DidNotReceiveWithAnyArgs().ReleaseStockReservation(default);
        await _basket.DidNotReceiveWithAnyArgs().ReleaseBasketCheckoutAsync(default, default);
    }

    // ---------- fejl-sporet: en afvisning sender checkouten til Failed efter oprydning ----------

    [Fact]
    public async Task CatalogRejects_ReleasesBasketAndFails()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        ReleasesSucceed();
        _catalog.GetCatalogItemsByIdsAsync(Arg.Any<List<int>>())
            .Returns(new GetCatalogItemsResult { IsSuccess = false, Reason = "Catalog down" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.Failed, checkout.State);
        Assert.Contains("Catalog down", checkout.FailureReason);
        await _basket.Received(1).ReleaseBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);
        await _inventory.DidNotReceiveWithAnyArgs().ReleaseStockReservation(default);
        await _inventory.DidNotReceiveWithAnyArgs().ReserveStock(default, default, default!);
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task CatalogMissingAnItem_ReleasesBasketAndFails()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        ReleasesSucceed();
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

        Assert.Equal(CheckoutState.Failed, checkout.State);
        Assert.Contains("2", checkout.FailureReason);
        await _basket.Received(1).ReleaseBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);
        await _inventory.DidNotReceiveWithAnyArgs().ReserveStock(default, default, default!);
    }

    [Fact]
    public async Task InventoryRejectsReservation_RejectsOrderReleasesBasketOnlyAndFails()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        CatalogReturnsBothItems();
        ReleasesSucceed();
        _inventory.ReserveStock(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<List<StockLineDto>>())
            .Returns(new ReserveStockResult { IsSuccess = false, Reason = "Not enough stock" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.Failed, checkout.State);
        Assert.Contains("Not enough stock", checkout.FailureReason);
        Assert.Equal(OrderStatus.Rejected, _savedOrder!.Status);
        await _basket.Received(1).ReleaseBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);
        // Intet lager blev holdt, så der er intet lager at frigive
        await _inventory.DidNotReceiveWithAnyArgs().ReleaseStockReservation(default);
        await _inventory.DidNotReceiveWithAnyArgs().ConfirmStockReservation(default);
    }

    [Fact]
    public async Task InventoryRejectsConfirmation_RejectsOrderThenReleasesStockThenBasketAndFails()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        CatalogReturnsBothItems();
        ReleasesSucceed();
        _inventory.ReserveStock(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<List<StockLineDto>>())
            .Returns(new ReserveStockResult { IsSuccess = true });
        _inventory.ConfirmStockReservation(Arg.Any<Guid>())
            .Returns(new ConfirmStockReservationResult { IsSuccess = false, Reason = "Reservation expired" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.Failed, checkout.State);
        Assert.Contains("Reservation expired", checkout.FailureReason);
        Assert.Equal(OrderStatus.Rejected, _savedOrder!.Status);

        // Oprydningen sker i omvendt rækkefølge: først lager, så kurv
        Received.InOrder(() =>
        {
            _inventory.ReleaseStockReservation(checkout.CheckoutId);
            _basket.ReleaseBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);
        });
        await _basket.DidNotReceiveWithAnyArgs().CompleteBasketCheckoutAsync(default, default);
    }

    [Fact]
    public async Task ReleaseStockFails_StaysInReleasingStockAndDoesNotReleaseBasketYet()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        CatalogReturnsBothItems();
        _inventory.ReserveStock(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<List<StockLineDto>>())
            .Returns(new ReserveStockResult { IsSuccess = true });
        _inventory.ConfirmStockReservation(Arg.Any<Guid>())
            .Returns(new ConfirmStockReservationResult { IsSuccess = false, Reason = "Reservation expired" });
        _inventory.ReleaseStockReservation(Arg.Any<Guid>())
            .Returns(new ReleaseStockReservationResult { IsSuccess = false, Reason = "Inventory down" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.ReleasingStock, checkout.State);
        await _basket.DidNotReceiveWithAnyArgs().ReleaseBasketCheckoutAsync(default, default);
    }

    [Fact]
    public async Task ReleaseBasketFails_StaysInReleasingBasket()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        _catalog.GetCatalogItemsByIdsAsync(Arg.Any<List<int>>())
            .Returns(new GetCatalogItemsResult { IsSuccess = false, Reason = "Catalog down" });
        _basket.ReleaseBasketCheckoutAsync(Arg.Any<int>(), Arg.Any<Guid>())
            .Returns(new ReleaseBasketCheckoutResult { IsSuccess = false, Reason = "Basket down" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);
    }

    [Fact]
    public async Task CheckoutStuckInReleasingBasket_CompletesTheCleanupWhenAdvancedAgain()
    {
        var checkout = FreshCheckout();
        checkout.MoveTo(CheckoutState.ReleasingBasket);
        CheckoutIs(checkout);
        ReleasesSucceed();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.Failed, checkout.State);
    }

    // ---------- succesfuld bekræftelse, men oprydning hænger ----------

    [Fact]
    public async Task BasketCleanupFails_OrderIsStillConfirmedAndCheckoutWaitsForCleanup()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);
        CatalogReturnsBothItems();
        InventoryAcceptsReserveAndConfirm();
        _basket.CompleteBasketCheckoutAsync(Arg.Any<int>(), Arg.Any<Guid>())
            .Returns(new CompleteBasketCheckoutResult { IsSuccess = false, Reason = "Basket service down" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.OrderConfirmedCleanupPending, checkout.State);
        Assert.Equal(OrderStatus.Confirmed, _savedOrder!.Status);
        // Ordren er bekræftet, så fejl-sporet må ikke køre
        await _inventory.DidNotReceiveWithAnyArgs().ReleaseStockReservation(default);
        await _basket.DidNotReceiveWithAnyArgs().ReleaseBasketCheckoutAsync(default, default);
    }

    // ---------- kanttilfælde ----------

    [Fact]
    public async Task UnknownCheckout_DoesNothing()
    {
        CheckoutIs(null);

        await _manager.AdvanceOrderAsync(Guid.NewGuid());

        await _producer.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default, default, default!);
        await _checkoutRepo.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Theory]
    [InlineData(CheckoutState.Completed)]
    [InlineData(CheckoutState.Failed)]
    public async Task FinishedCheckout_DoesNotCallAnyone(CheckoutState finalState)
    {
        var checkout = FreshCheckout();
        checkout.MoveTo(finalState);
        CheckoutIs(checkout);

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(finalState, checkout.State);
        await _producer.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default, default, default!);
        await _basket.DidNotReceiveWithAnyArgs().ReleaseBasketCheckoutAsync(default, default);
        await _inventory.DidNotReceiveWithAnyArgs().ReserveStock(default, default, default!);
        await _inventory.DidNotReceiveWithAnyArgs().ReleaseStockReservation(default);
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }
}
