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
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Services;

public class CheckoutProcessManagerTests
{
    private const int TestOrderId = 7;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<CheckoutProcess> _checkoutRepo = Substitute.For<IRepository<CheckoutProcess>>();
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly ICheckoutRequestProducer _producer = Substitute.For<ICheckoutRequestProducer>();
    private readonly CheckoutProcessManager _manager;

    // Den ordre manageren opretter, så vi kan finde den igen og kontrollere dens status
    private Order? _savedOrder;

    public CheckoutProcessManagerTests()
    {
        _manager = new CheckoutProcessManager(_checkoutRepo, _orderRepo, _producer);

        // Når manageren gemmer en ordre, husker vi den og giver den et id (databasen gør det i virkeligheden) ...
        _orderRepo.AddAsync(Arg.Do<Order>(o => _savedOrder = o.WithId(TestOrderId)), Arg.Any<CancellationToken>());

        // ... og når den slår ordren op igen, får den den samme ordre tilbage
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _savedOrder);
    }

    // ---------- hjælpere ----------

    /// <summary>En besked, manageren har sendt til en anden service.</summary>
    private sealed record Sent(string Queue, string MessageType, Guid CheckoutId, Guid CorrelationId, object Payload);

    /// <summary>Alle beskeder manageren har sendt indtil nu, i rækkefølge.</summary>
    private List<Sent> SentMessages() =>
        _producer.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ICheckoutRequestProducer.SendAsync))
            .Select(c => c.GetArguments())
            .Select(a => new Sent((string)a[0]!, (string)a[1]!, (Guid)a[2]!, (Guid)a[3]!, a[4]!))
            .ToList();

    private List<string> SentTypes() => SentMessages().Select(s => s.MessageType).ToList();

    /// <summary>En helt ny checkout, der står i AwaitingBasket og endnu ikke har fået svar fra Basket.</summary>
    private static CheckoutProcess FreshCheckout() =>
        new(Guid.NewGuid(), basketId: 1, buyerId: "test@test.dk", new AddressBuilder().WithDefaultValues());

    /// <summary>
    /// En checkout, hvor Basket allerede har svaret: den har to varelinjer og står i AwaitingCatalog.
    /// </summary>
    private static CheckoutProcess CheckoutAfterBasketReply()
    {
        var checkout = FreshCheckout();
        checkout.SetLines(new[] { new CheckoutLine(1, 2), new CheckoutLine(2, 1) });
        checkout.MoveTo(CheckoutState.AwaitingCatalog);
        return checkout;
    }

    /// <summary>
    /// En checkout, hvor Catalog også har svaret: linjerne har navn, pris og billede, og den står i
    /// AwaitingStockReservation. Bruges af de tests, der starter ved Inventory-trinnet.
    /// </summary>
    private static CheckoutProcess CheckoutAfterCatalogReply()
    {
        var checkout = CheckoutAfterBasketReply();
        foreach (var line in checkout.Lines)
        {
            line.SetCatalogSnapshot($"Item {line.CatalogItemId}", line.CatalogItemId * 10m, $"http://x/{line.CatalogItemId}.jpg");
        }
        checkout.MoveTo(CheckoutState.AwaitingStockReservation);
        return checkout;
    }

    /// <summary>Manageren har oprettet ordren, sendt ReserveStock og venter på Inventory.</summary>
    private async Task<CheckoutProcess> CheckoutWaitingForReservationReply()
    {
        var checkout = CheckoutAfterCatalogReply();
        CheckoutIs(checkout);
        await _manager.AdvanceOrderAsync(checkout.CheckoutId);
        return checkout;
    }

    /// <summary>Inventory har svaret StockReserved; manageren har sendt ConfirmStockReservation og venter.</summary>
    private async Task<CheckoutProcess> CheckoutWaitingForConfirmationReply()
    {
        var checkout = await CheckoutWaitingForReservationReply();
        await ReplyAsync(checkout, "StockReserved", new StockReservedPayload { OrderId = TestOrderId });
        return checkout;
    }

    /// <summary>Inventory har bekræftet; manageren har sendt CompleteBasketCheckout og venter på Basket.</summary>
    private async Task<CheckoutProcess> CheckoutWaitingForBasketCleanupReply()
    {
        var checkout = await CheckoutWaitingForConfirmationReply();
        await ReplyAsync(checkout, "StockReservationConfirmed", new StockReservationConfirmedPayload());
        return checkout;
    }

    /// <summary>Sender et svar, der bærer det CorrelationId, checkouten venter på.</summary>
    private Task ReplyAsync(CheckoutProcess checkout, string messageType, object payload) =>
        _manager.HandleReplyAsync(ReplyEnvelope(checkout, checkout.ExpectedCorrelationId!.Value, messageType, payload));

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

    private static CatalogItemsReturnedPayload BothCatalogItems() =>
        new()
        {
            Items = new List<CatalogItemDto>
            {
                new() { CatalogItemId = 1, Name = "Item 1", Price = 10m, PictureUri = "http://x/1.jpg" },
                new() { CatalogItemId = 2, Name = "Item 2", Price = 20m, PictureUri = "http://x/2.jpg" }
            }
        };

    private static CatalogItemsReturnedPayload OnlyItemOne() =>
        new()
        {
            Items = new List<CatalogItemDto>
            {
                new() { CatalogItemId = 1, Name = "Item 1", Price = 10m, PictureUri = "http://x/1.jpg" }
            }
        };

    private void CheckoutIs(CheckoutProcess? checkout)
    {
        _checkoutRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<CheckoutProcess>>(), Arg.Any<CancellationToken>())
            .Returns(checkout);
    }

    // ---------- Basket: prepare ----------

    [Fact]
    public async Task AwaitingBasket_SendsPrepareRequestToBasketAndWaits()
    {
        var checkout = FreshCheckout();
        CheckoutIs(checkout);

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        var sent = Assert.Single(SentMessages());
        Assert.Equal(CheckoutQueues.Basket, sent.Queue);
        Assert.Equal("PrepareBasketForCheckout", sent.MessageType);
        Assert.Equal(checkout.CheckoutId, sent.CheckoutId);

        var payload = Assert.IsType<PrepareBasketForCheckoutPayload>(sent.Payload);
        Assert.Equal(checkout.BasketId, payload.BasketId);
        Assert.Equal(checkout.BuyerId, payload.BuyerId);

        // Checkouten venter på svaret og husker, hvilket svar det er
        Assert.Equal(CheckoutState.AwaitingBasket, checkout.State);
        Assert.Equal(sent.CorrelationId, checkout.ExpectedCorrelationId);
    }

    [Fact]
    public async Task BasketPreparedReply_SavesLinesAndSendsTheCatalogRequest()
    {
        var checkout = FreshCheckout();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, correlationId, "BasketPrepared", TwoBasketLines()));

        Assert.Equal(2, checkout.Lines.Count);
        Assert.Contains(checkout.Lines, l => l.CatalogItemId == 1 && l.Quantity == 2);
        Assert.Contains(checkout.Lines, l => l.CatalogItemId == 2 && l.Quantity == 1);

        Assert.Equal(CheckoutState.AwaitingCatalog, checkout.State);
        Assert.Equal(new[] { "GetCatalogItemsByIds" }, SentTypes());
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
        Assert.Empty(SentMessages());
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

    // ---------- Catalog ----------

    [Fact]
    public async Task AwaitingCatalog_SendsGetCatalogItemsRequestAndWaits()
    {
        var checkout = CheckoutAfterBasketReply();
        CheckoutIs(checkout);

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        var sent = Assert.Single(SentMessages());
        Assert.Equal(CheckoutQueues.Catalog, sent.Queue);
        Assert.Equal("GetCatalogItemsByIds", sent.MessageType);

        var payload = Assert.IsType<GetCatalogItemsByIdsPayload>(sent.Payload);
        Assert.Equal(new[] { 1, 2 }, payload.CatalogItemIds.OrderBy(i => i));

        Assert.Equal(CheckoutState.AwaitingCatalog, checkout.State);
        Assert.Equal(sent.CorrelationId, checkout.ExpectedCorrelationId);
    }

    [Fact]
    public async Task AwaitingCatalog_SendsEachItemIdOnlyOnce()
    {
        var checkout = FreshCheckout();
        checkout.SetLines(new[] { new CheckoutLine(1, 2), new CheckoutLine(1, 3), new CheckoutLine(2, 1) });
        checkout.MoveTo(CheckoutState.AwaitingCatalog);
        CheckoutIs(checkout);

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        var payload = Assert.IsType<GetCatalogItemsByIdsPayload>(Assert.Single(SentMessages()).Payload);
        Assert.Equal(new[] { 1, 2 }, payload.CatalogItemIds.OrderBy(i => i));
    }

    [Fact]
    public async Task CatalogItemsReturnedReply_SavesSnapshotAndSendsTheReserveRequest()
    {
        var checkout = CheckoutAfterBasketReply();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, correlationId, "CatalogItemsReturned", BothCatalogItems()));

        var line1 = Assert.Single(checkout.Lines, l => l.CatalogItemId == 1);
        Assert.Equal("Item 1", line1.ProductName);
        Assert.Equal(10m, line1.UnitPrice);
        Assert.Equal("http://x/1.jpg", line1.PictureUri);
        Assert.Equal(2, checkout.Lines.Count);

        Assert.Equal(CheckoutState.AwaitingStockReservation, checkout.State);
        Assert.Equal(new[] { "ReserveStock" }, SentTypes());
    }

    [Fact]
    public async Task CatalogReply_WithWrongCorrelationId_IsIgnored()
    {
        var checkout = CheckoutAfterBasketReply();
        checkout.ExpectReply(Guid.NewGuid());
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, Guid.NewGuid(), "CatalogItemsReturned", BothCatalogItems()));

        Assert.Equal(CheckoutState.AwaitingCatalog, checkout.State);
        Assert.All(checkout.Lines, l => Assert.Null(l.ProductName));
        Assert.Empty(SentMessages());
    }

    [Fact]
    public async Task CatalogReply_MissingAnItem_FailsAndReleasesTheBasket()
    {
        var checkout = CheckoutAfterBasketReply();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);

        // Catalog kender kun vare 1, selvom kurven indeholder 1 og 2
        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, correlationId, "CatalogItemsReturned", OnlyItemOne()));

        // Fejlen er gemt, og manageren beder Basket frigive kurven og venter
        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);
        Assert.Contains("2", checkout.FailureReason);
        var sent = Assert.Single(SentMessages());
        Assert.Equal(CheckoutQueues.Basket, sent.Queue);
        Assert.Equal("ReleaseBasketCheckout", sent.MessageType);
        var payload = Assert.IsType<ReleaseBasketCheckoutPayload>(sent.Payload);
        Assert.Equal(checkout.BasketId, payload.BasketId);
        Assert.Equal(checkout.FailureReason, payload.Reason);
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);

        // Basket bekræfter frigivelsen -> færdig
        await ReplyAsync(checkout, "BasketCheckoutReleased", new BasketCheckoutReleasedPayload());
        Assert.Equal(CheckoutState.Failed, checkout.State);
    }

    [Fact]
    public async Task CatalogReply_LookupRejected_FailsAndReleasesTheBasket()
    {
        var checkout = CheckoutAfterBasketReply();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(
            checkout, correlationId, "CatalogLookupRejected", new CatalogLookupRejectedPayload { Reason = "Catalog unavailable" }));

        // Kurven er låst, så den skal frigives
        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);
        Assert.Contains("Catalog unavailable", checkout.FailureReason);
        var sent = Assert.Single(SentMessages());
        Assert.Equal(CheckoutQueues.Basket, sent.Queue);
        Assert.Equal("ReleaseBasketCheckout", sent.MessageType);
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);

        await ReplyAsync(checkout, "BasketCheckoutReleased", new BasketCheckoutReleasedPayload());
        Assert.Equal(CheckoutState.Failed, checkout.State);
    }

    // ---------- Basket: afvisning ----------

    [Fact]
    public async Task BasketReply_PreparationRejected_FailsWithoutReleasing()
    {
        var checkout = FreshCheckout();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(
            checkout, correlationId, "BasketPreparationRejected", new BasketPreparationRejectedPayload { Reason = "Basket is empty" }));

        // Intet er låst, så der er intet at frigive: direkte til Failed og ingen nye requests
        Assert.Equal(CheckoutState.Failed, checkout.State);
        Assert.Contains("Basket is empty", checkout.FailureReason);
        Assert.Empty(SentMessages());
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    // ---------- Inventory: reservation ----------

    [Fact]
    public async Task AwaitingStockReservation_CreatesPendingOrderAndSendsReserveRequest()
    {
        var checkout = CheckoutAfterCatalogReply();
        CheckoutIs(checkout);

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        // Den ventende ordre findes, før der reserveres (Inventory skal bruge ordrens id)
        Assert.NotNull(_savedOrder);
        Assert.Equal(OrderStatus.PendingInventory, _savedOrder!.Status);
        Assert.Equal(checkout.CheckoutId, _savedOrder.CheckoutId);
        Assert.Equal(2, _savedOrder.OrderItems.Count);

        var sent = Assert.Single(SentMessages());
        Assert.Equal(CheckoutQueues.Inventory, sent.Queue);
        Assert.Equal("ReserveStock", sent.MessageType);

        var payload = Assert.IsType<ReserveStockPayload>(sent.Payload);
        Assert.Equal(TestOrderId, payload.OrderId);
        Assert.Equal(2, payload.Items.Count);
        Assert.Contains(payload.Items, i => i.CatalogItemId == 1 && i.Quantity == 2);

        Assert.Equal(CheckoutState.AwaitingStockReservation, checkout.State);
        Assert.Equal(sent.CorrelationId, checkout.ExpectedCorrelationId);
    }

    [Fact]
    public async Task StockReservedReply_MovesToConfirmationAndSendsConfirmRequest()
    {
        var checkout = await CheckoutWaitingForReservationReply();

        await ReplyAsync(checkout, "StockReserved", new StockReservedPayload { OrderId = TestOrderId });

        Assert.Equal(CheckoutState.AwaitingStockConfirmation, checkout.State);
        var sent = SentMessages().Last();
        Assert.Equal(CheckoutQueues.Inventory, sent.Queue);
        Assert.Equal("ConfirmStockReservation", sent.MessageType);
        var payload = Assert.IsType<ConfirmStockReservationPayload>(sent.Payload);
        Assert.Equal(TestOrderId, payload.OrderId);
    }

    [Fact]
    public async Task StockReservationRejectedReply_RejectsOrderAndReleasesOnlyTheBasket()
    {
        var checkout = await CheckoutWaitingForReservationReply();

        await ReplyAsync(checkout, "StockReservationRejected", new StockReservationRejectedPayload { Reason = "Not enough stock" });

        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);
        Assert.Contains("Not enough stock", checkout.FailureReason);
        Assert.Equal(OrderStatus.Rejected, _savedOrder!.Status);

        // Intet lager blev holdt, så der er intet lager at frigive: kun kurven frigives
        Assert.Equal("ReleaseBasketCheckout", SentMessages().Last().MessageType);
        Assert.DoesNotContain("ReleaseStockReservation", SentTypes());

        await ReplyAsync(checkout, "BasketCheckoutReleased", new BasketCheckoutReleasedPayload());
        Assert.Equal(CheckoutState.Failed, checkout.State);
    }

    [Fact]
    public async Task StockReservedReply_WithWrongCorrelationId_IsIgnored()
    {
        var checkout = await CheckoutWaitingForReservationReply();
        var sentBefore = SentMessages().Count;

        await _manager.HandleReplyAsync(ReplyEnvelope(
            checkout, Guid.NewGuid(), "StockReserved", new StockReservedPayload { OrderId = TestOrderId }));

        Assert.Equal(CheckoutState.AwaitingStockReservation, checkout.State);
        Assert.Equal(sentBefore, SentMessages().Count);
    }

    // ---------- Inventory: bekræftelse og Basket complete ----------

    [Fact]
    public async Task StockReservationConfirmedReply_ConfirmsOrderAndSendsCompleteBasketRequest()
    {
        var checkout = await CheckoutWaitingForConfirmationReply();

        await ReplyAsync(checkout, "StockReservationConfirmed", new StockReservationConfirmedPayload());

        Assert.Equal(OrderStatus.Confirmed, _savedOrder!.Status);
        Assert.Equal(CheckoutState.OrderConfirmedCleanupPending, checkout.State);

        var sent = SentMessages().Last();
        Assert.Equal(CheckoutQueues.Basket, sent.Queue);
        Assert.Equal("CompleteBasketCheckout", sent.MessageType);
        var payload = Assert.IsType<CompleteBasketCheckoutPayload>(sent.Payload);
        Assert.Equal(checkout.BasketId, payload.BasketId);
        Assert.Equal(checkout.BuyerId, payload.BuyerId);
        Assert.Equal(TestOrderId, payload.OrderId);
    }

    [Fact]
    public async Task BasketCheckoutCompletedReply_Completes()
    {
        var checkout = await CheckoutWaitingForBasketCleanupReply();

        await ReplyAsync(checkout, "BasketCheckoutCompleted", new BasketCheckoutCompletedPayload());

        Assert.Equal(CheckoutState.Completed, checkout.State);
        Assert.Null(checkout.FailureReason);
        Assert.Equal(OrderStatus.Confirmed, _savedOrder!.Status);
    }

    [Fact]
    public async Task BasketCleanupRejected_OrderStaysConfirmedAndCheckoutWaitsForCleanup()
    {
        var checkout = await CheckoutWaitingForBasketCleanupReply();

        // Basket afviser oprydningen (svaret kendes ikke af manageren og ignoreres)
        await ReplyAsync(checkout, "BasketCompletionRejected", new { reason = "Basket service busy" });

        Assert.Equal(CheckoutState.OrderConfirmedCleanupPending, checkout.State);
        Assert.Equal(OrderStatus.Confirmed, _savedOrder!.Status);
        // Ordren er bekræftet, så fejl-sporet må ikke køre
        Assert.DoesNotContain("ReleaseStockReservation", SentTypes());
        Assert.DoesNotContain("ReleaseBasketCheckout", SentTypes());
    }

    [Fact]
    public async Task CheckoutStuckInCleanupPending_ResendsTheCompleteRequestWhenAdvancedAgain()
    {
        var checkout = await CheckoutWaitingForBasketCleanupReply();

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(2, SentTypes().Count(t => t == "CompleteBasketCheckout"));
        Assert.Equal(CheckoutState.OrderConfirmedCleanupPending, checkout.State);
    }

    // ---------- deadline: annullering ----------

    [Theory]
    [InlineData(CheckoutState.AwaitingBasket)]
    [InlineData(CheckoutState.AwaitingCatalog)]
    public async Task Cancel_BeforeAnyStockIsReserved_ReleasesOnlyTheBasket(CheckoutState state)
    {
        var checkout = FreshCheckout();
        checkout.MoveTo(state);
        CheckoutIs(checkout);

        await _manager.CancelAsync(checkout.CheckoutId);

        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);
        Assert.Equal("Checkout timed out", checkout.FailureReason);
        var sent = Assert.Single(SentMessages());
        Assert.Equal(CheckoutQueues.Basket, sent.Queue);
        Assert.Equal("ReleaseBasketCheckout", sent.MessageType);
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);

        await ReplyAsync(checkout, "BasketCheckoutReleased", new BasketCheckoutReleasedPayload());
        Assert.Equal(CheckoutState.Failed, checkout.State);
    }

    [Fact]
    public async Task Cancel_WhileWaitingForInventory_RejectsOrderAndReleasesStockThenBasket()
    {
        var checkout = await CheckoutWaitingForReservationReply();

        await _manager.CancelAsync(checkout.CheckoutId);

        // Reservationen kan være nået frem, selv om svaret udeblev, så lageret frigives først
        Assert.Equal(CheckoutState.ReleasingStock, checkout.State);
        Assert.Equal(OrderStatus.Rejected, _savedOrder!.Status);
        var sent = SentMessages().Last();
        Assert.Equal(CheckoutQueues.Inventory, sent.Queue);
        Assert.Equal("ReleaseStockReservation", sent.MessageType);

        await ReplyAsync(checkout, "StockReservationReleased", new StockReservationReleasedPayload());
        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);

        await ReplyAsync(checkout, "BasketCheckoutReleased", new BasketCheckoutReleasedPayload());
        Assert.Equal(CheckoutState.Failed, checkout.State);
        Assert.Equal(
            new[] { "ReserveStock", "ReleaseStockReservation", "ReleaseBasketCheckout" },
            SentTypes());
    }

    [Fact]
    public async Task Cancel_LateReplyFromBeforeTheCancellation_IsIgnored()
    {
        var checkout = await CheckoutWaitingForConfirmationReply();
        var oldCorrelationId = checkout.ExpectedCorrelationId!.Value;

        await _manager.CancelAsync(checkout.CheckoutId);
        var sentBefore = SentMessages().Count;

        // Inventory svarer for sent på den gamle ConfirmStockReservation
        await _manager.HandleReplyAsync(ReplyEnvelope(
            checkout, oldCorrelationId, "StockReservationConfirmed", new StockReservationConfirmedPayload()));

        Assert.Equal(CheckoutState.ReleasingStock, checkout.State);
        Assert.Equal(OrderStatus.Rejected, _savedOrder!.Status);
        Assert.Equal(sentBefore, SentMessages().Count);
    }

    [Theory]
    [InlineData(CheckoutState.OrderConfirmedCleanupPending)]
    [InlineData(CheckoutState.Completed)]
    [InlineData(CheckoutState.ReleasingBasket)]
    [InlineData(CheckoutState.Failed)]
    public async Task Cancel_AfterConfirmationOrWhenAlreadyCleaningUp_DoesNothing(CheckoutState state)
    {
        var checkout = FreshCheckout();
        checkout.MoveTo(state);
        CheckoutIs(checkout);

        await _manager.CancelAsync(checkout.CheckoutId);

        Assert.Equal(state, checkout.State);
        Assert.Empty(SentMessages());
        Assert.Null(checkout.FailureReason);
    }

    [Fact]
    public async Task Cancel_UnknownCheckout_DoesNothing()
    {
        CheckoutIs(null);

        await _manager.CancelAsync(Guid.NewGuid());

        Assert.Empty(SentMessages());
    }

    // ---------- fejl-sporet: afvist bekræftelse ----------

    [Fact]
    public async Task StockConfirmationRejectedReply_RejectsOrderAndSendsReleaseStockRequest()
    {
        var checkout = await CheckoutWaitingForConfirmationReply();

        await ReplyAsync(checkout, "StockConfirmationRejected", new StockConfirmationRejectedPayload { Reason = "Reservation expired" });

        Assert.Equal(CheckoutState.ReleasingStock, checkout.State);
        Assert.Contains("Reservation expired", checkout.FailureReason);
        Assert.Equal(OrderStatus.Rejected, _savedOrder!.Status);

        var sent = SentMessages().Last();
        Assert.Equal(CheckoutQueues.Inventory, sent.Queue);
        Assert.Equal("ReleaseStockReservation", sent.MessageType);
        var payload = Assert.IsType<ReleaseStockReservationPayload>(sent.Payload);
        Assert.Equal(TestOrderId, payload.OrderId);
        Assert.Equal(checkout.FailureReason, payload.Reason);
    }

    [Fact]
    public async Task FailureTrack_ReleasesStockThenBasketInThatOrderAndEndsInFailed()
    {
        var checkout = await CheckoutWaitingForConfirmationReply();

        await ReplyAsync(checkout, "StockConfirmationRejected", new StockConfirmationRejectedPayload { Reason = "Reservation expired" });
        await ReplyAsync(checkout, "StockReservationReleased", new StockReservationReleasedPayload());

        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);

        await ReplyAsync(checkout, "BasketCheckoutReleased", new BasketCheckoutReleasedPayload());

        Assert.Equal(CheckoutState.Failed, checkout.State);
        Assert.Equal(
            new[] { "ReserveStock", "ConfirmStockReservation", "ReleaseStockReservation", "ReleaseBasketCheckout" },
            SentTypes());
        Assert.DoesNotContain("CompleteBasketCheckout", SentTypes());
    }

    [Fact]
    public async Task ReleaseStockNotAnswered_StaysInReleasingStockAndDoesNotReleaseBasketYet()
    {
        var checkout = await CheckoutWaitingForConfirmationReply();

        await ReplyAsync(checkout, "StockConfirmationRejected", new StockConfirmationRejectedPayload { Reason = "Reservation expired" });

        Assert.Equal(CheckoutState.ReleasingStock, checkout.State);
        Assert.DoesNotContain("ReleaseBasketCheckout", SentTypes());
    }

    [Fact]
    public async Task CheckoutStuckInReleasingStock_ResendsTheReleaseRequestWhenAdvancedAgain()
    {
        var checkout = await CheckoutWaitingForConfirmationReply();
        await ReplyAsync(checkout, "StockConfirmationRejected", new StockConfirmationRejectedPayload { Reason = "Reservation expired" });

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(2, SentTypes().Count(t => t == "ReleaseStockReservation"));
        Assert.Equal(CheckoutState.ReleasingStock, checkout.State);
    }

    [Fact]
    public async Task CheckoutStuckInReleasingBasket_SendsTheReleaseRequestWhenAdvanced()
    {
        var checkout = FreshCheckout();
        checkout.MoveTo(CheckoutState.ReleasingBasket);
        CheckoutIs(checkout);

        await _manager.AdvanceOrderAsync(checkout.CheckoutId);

        Assert.Equal(new[] { "ReleaseBasketCheckout" }, SentTypes());
        Assert.Equal(CheckoutState.ReleasingBasket, checkout.State);
    }

    // ---------- hele kæden ----------

    [Fact]
    public async Task HappyPath_FromCatalogReplyToCompleted_ThroughRealMessages()
    {
        var checkout = CheckoutAfterBasketReply();
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        CheckoutIs(checkout);

        await _manager.HandleReplyAsync(ReplyEnvelope(checkout, correlationId, "CatalogItemsReturned", BothCatalogItems()));
        await ReplyAsync(checkout, "StockReserved", new StockReservedPayload { OrderId = TestOrderId });
        await ReplyAsync(checkout, "StockReservationConfirmed", new StockReservationConfirmedPayload());
        await ReplyAsync(checkout, "BasketCheckoutCompleted", new BasketCheckoutCompletedPayload());

        Assert.Equal(CheckoutState.Completed, checkout.State);
        Assert.Null(checkout.FailureReason);

        Assert.NotNull(_savedOrder);
        Assert.Equal(OrderStatus.Confirmed, _savedOrder!.Status);
        Assert.Equal(checkout.CheckoutId, _savedOrder.CheckoutId);
        Assert.Equal(2, _savedOrder.OrderItems.Count);
        Assert.Equal(40m, _savedOrder.Total()); // 10*2 + 20*1

        // Præcis de beskeder, planen foreskriver, i rækkefølge, og fejl-sporet kørte ikke
        Assert.Equal(
            new[] { "ReserveStock", "ConfirmStockReservation", "CompleteBasketCheckout" },
            SentTypes());
    }

    // ---------- kanttilfælde ----------

    [Fact]
    public async Task UnknownCheckout_DoesNothing()
    {
        CheckoutIs(null);

        await _manager.AdvanceOrderAsync(Guid.NewGuid());

        Assert.Empty(SentMessages());
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
        Assert.Empty(SentMessages());
        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }
}
