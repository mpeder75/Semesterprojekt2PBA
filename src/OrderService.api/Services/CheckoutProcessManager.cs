using System.Text.Json;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Messages;
using OrderService.api.Messages.Basket;
using OrderService.api.Messages.Catalog;
using OrderService.api.Messages.Inventory;
using OrderService.api.Specifications;

namespace OrderService.api.Services;

/// <summary>
/// Process Manager for checkout. Styrer flowet gennem tilstandene og gemmer fremdriften i databasen.
/// Manageren sender requests til Basket, Catalog og Inventory over RabbitMQ og går videre, når svarene kommer
/// tilbage (<see cref="HandleReplyAsync"/>). Går noget galt, rydder den op i omvendt rækkefølge.
/// </summary>
/// <remarks>
/// Normal vej: AwaitingBasket, AwaitingCatalog, AwaitingStockReservation, AwaitingStockConfirmation,
/// OrderConfirmedCleanupPending, Completed. Fejl-sporet: ReleasingStock, ReleasingBasket, Failed.
/// Hver handler sender én request og venter (returnerer false). Svaret flytter tilstanden og kører videre.
/// </remarks>
public class CheckoutProcessManager
{
    private readonly IRepository<CheckoutProcess> _repository;
    private readonly IRepository<Order> _orderRepository;
    private readonly ICheckoutRequestProducer _producer;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Modtager de afhængigheder, manageren bruger. De leveres af .NET (dependency injection).
    /// </summary>
    public CheckoutProcessManager(IRepository<CheckoutProcess> repository, IRepository<Order> orderRepository,
        ICheckoutRequestProducer producer)
    {
        _repository = repository;
        _orderRepository = orderRepository;
        _producer = producer;
    }

    /// <summary>
    /// Finder checkouten og kører den videre, trin for trin, indtil et trin stopper eller flowet er færdigt.
    /// </summary>
    /// <remarks>
    /// Hver tilstand har sin egen handler. En handler returnerer true, når flowet skal fortsætte med den
    /// nuværende tilstand, og false, når det skal stoppe (typisk fordi en request er sendt, og vi venter på svar).
    /// Completed og Failed er slutninger.
    /// </remarks>
    public async Task AdvanceOrderAsync(Guid checkoutId)
    {
        var checkout = await _repository.FirstOrDefaultAsync(new CheckoutProcessByCheckoutIdSpec(checkoutId));

        if (checkout is null)
        {
            return;
        }

        while (true)
        {
            switch (checkout.State)
            {
                case CheckoutState.AwaitingBasket:
                    if (!await HandleAwaitingBasketAsync(checkout))
                    {
                        return;
                    }
                    break;

                case CheckoutState.AwaitingCatalog:
                    if (!await HandleAwaitingCatalogAsync(checkout))
                    {
                        return;
                    }
                    break;

                case CheckoutState.AwaitingStockReservation:
                    if (!await HandleAwaitingStockReservationAsync(checkout))
                    {
                        return;
                    }
                    break;

                case CheckoutState.AwaitingStockConfirmation:
                    if (!await HandleAwaitingStockConfirmationAsync(checkout))
                    {
                        return;
                    }
                    break;

                case CheckoutState.OrderConfirmedCleanupPending:
                    if (!await HandleOrderConfirmedCleanupPendingAsync(checkout))
                    {
                        return;
                    }
                    break;

                case CheckoutState.ReleasingStock:
                    if (!await HandleReleasingStockAsync(checkout))
                    {
                        return;
                    }
                    break;

                case CheckoutState.ReleasingBasket:
                    if (!await HandleReleasingBasketAsync(checkout))
                    {
                        return;
                    }
                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Behandler et svar fra en anden service (kaldes af reply-consumeren).
    /// </summary>
    /// <remarks>
    /// Ignorerer svaret, hvis checkouten ikke findes, eller hvis svarets CorrelationId ikke er det, vi venter på
    /// (gammelt eller dobbelt svar). Derefter vælges handling efter MessageType:
    /// BasketPrepared, BasketPreparationRejected, CatalogItemsReturned, CatalogLookupRejected, StockReserved,
    /// StockReservationRejected, StockReservationConfirmed, StockConfirmationRejected, BasketCheckoutCompleted,
    /// StockReservationReleased og BasketCheckoutReleased.
    /// Afviser Basket forberedelsen (BasketPreparationRejected), går checkouten direkte til Failed, for intet er låst.
    /// Afviser Catalog opslaget (CatalogLookupRejected), frigives kurven (ReleasingBasket), for den er allerede låst.
    /// Ukendte svar ignoreres. En afvisning af oprydningen (Complete/Release) ignoreres også, så checkouten bliver
    /// stående i sin tilstand og kan forsøges igen ved at køre AdvanceOrderAsync.
    /// </remarks>
    public async Task HandleReplyAsync(CheckoutMessageEnvelope envelope)
    {
        var checkout = await _repository.FirstOrDefaultAsync(new CheckoutProcessByCheckoutIdSpec(envelope.CheckoutId));

        if (checkout is null)
        {
            return;
        }

        if (checkout.ExpectedCorrelationId != envelope.CorrelationId)
        {
            return;
        }

        switch (envelope.MessageType)
        {
            case "BasketPrepared":
                var payloadBasket = envelope.Payload.Deserialize<BasketPreparedPayload>(JsonOptions);

                if (payloadBasket is null)
                {
                    return;
                }

                var checkOutLines = payloadBasket.Items.Select(i => new CheckoutLine(i.CatalogItemId, i.Quantity));
                checkout.SetLines(checkOutLines);
                checkout.MoveTo(CheckoutState.AwaitingCatalog);

                await _repository.UpdateAsync(checkout);

                await AdvanceOrderAsync(checkout.CheckoutId);
                break;

            case "CatalogItemsReturned":
                var payloadCatalog = envelope.Payload.Deserialize<CatalogItemsReturnedPayload>(JsonOptions);

                if (payloadCatalog is null)
                {
                    return;
                }

                foreach (var line in checkout.Lines)
                {
                    var item = payloadCatalog.Items.FirstOrDefault(i => i.CatalogItemId == line.CatalogItemId);

                    if (item is null)
                    {
                        await FailAsync(checkout, $"Catalog does not know item {line.CatalogItemId}.", CheckoutState.ReleasingBasket);
                        await AdvanceOrderAsync(checkout.CheckoutId);
                        return;
                    }

                    line.SetCatalogSnapshot(item.Name, item.Price, item.PictureUri);
                }

                checkout.MoveTo(CheckoutState.AwaitingStockReservation);
                await _repository.UpdateAsync(checkout);

                await AdvanceOrderAsync(checkout.CheckoutId);
                break;

            case "StockReserved":
                var stockReserved = envelope.Payload.Deserialize<StockReservedPayload>(JsonOptions);

                if (stockReserved is null)
                {
                    return;
                }

                checkout.MoveTo(CheckoutState.AwaitingStockConfirmation);

                await _repository.UpdateAsync(checkout);
                await AdvanceOrderAsync(checkout.CheckoutId);
                break;

            case "StockReservationConfirmed":
                var stockReservationConfirmed = envelope.Payload.Deserialize<StockReservationConfirmedPayload>(JsonOptions);

                if (stockReservationConfirmed is null)
                {
                    return;
                }

                var confirmedOrder = await _orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkout.CheckoutId));

                if (confirmedOrder is null)
                {
                    return;
                }

                confirmedOrder.Confirm();
                await _orderRepository.UpdateAsync(confirmedOrder);

                checkout.MoveTo(CheckoutState.OrderConfirmedCleanupPending);
                await _repository.UpdateAsync(checkout);
                await AdvanceOrderAsync(checkout.CheckoutId);
                break;

            case "StockReservationRejected":
                var stockReservationRejected = envelope.Payload.Deserialize<StockReservationRejectedPayload>(JsonOptions);

                if (stockReservationRejected is null)
                {
                    return;
                }

                var rejectedOrder = await _orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkout.CheckoutId));

                if (rejectedOrder is not null)
                {
                    rejectedOrder.Reject();
                    await _orderRepository.UpdateAsync(rejectedOrder);
                }

                await FailAsync(checkout, $"Stock reservation failed: {stockReservationRejected.Reason}", CheckoutState.ReleasingBasket);
                await AdvanceOrderAsync(checkout.CheckoutId);
                break;

            case "StockConfirmationRejected":
                var stockConfirmationRejected = envelope.Payload.Deserialize<StockConfirmationRejectedPayload>(JsonOptions);

                if (stockConfirmationRejected is null)
                {
                    return;
                }

                var rejectedConfirmOrder = await _orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkout.CheckoutId));

                if (rejectedConfirmOrder is not null)
                {
                    rejectedConfirmOrder.Reject();
                    await _orderRepository.UpdateAsync(rejectedConfirmOrder);
                }

                await FailAsync(checkout, $"Stock confirmation failed: {stockConfirmationRejected.Reason}", CheckoutState.ReleasingStock);
                await AdvanceOrderAsync(checkout.CheckoutId);
                break;

            case "BasketCheckoutCompleted":
                var basketCompleted = envelope.Payload.Deserialize<BasketCheckoutCompletedPayload>(JsonOptions);

                if (basketCompleted is null)
                {
                    return;
                }

                checkout.MoveTo(CheckoutState.Completed);
                await _repository.UpdateAsync(checkout);
                break;

            case "StockReservationReleased":
                var stockReleased = envelope.Payload.Deserialize<StockReservationReleasedPayload>(JsonOptions);

                if (stockReleased is null)
                {
                    return;
                }

                checkout.MoveTo(CheckoutState.ReleasingBasket);
                await _repository.UpdateAsync(checkout);
                await AdvanceOrderAsync(checkout.CheckoutId);
                break;

            case "BasketCheckoutReleased":
                var basketReleased = envelope.Payload.Deserialize<BasketCheckoutReleasedPayload>(JsonOptions);

                if (basketReleased is null)
                {
                    return;
                }

                checkout.MoveTo(CheckoutState.Failed);
                await _repository.UpdateAsync(checkout);
                break;

            case "BasketPreparationRejected":
                var basketRejected = envelope.Payload.Deserialize<BasketPreparationRejectedPayload>(JsonOptions);

                if (basketRejected is null)
                {
                    return;
                }

                await FailAsync(checkout, $"Basket rejected the checkout: {basketRejected.Reason}", CheckoutState.Failed);
                break;

            case "CatalogLookupRejected":
                var catalogRejected = envelope.Payload.Deserialize<CatalogLookupRejectedPayload>(JsonOptions);

                if (catalogRejected is null)
                {
                    return;
                }

                await FailAsync(checkout, $"Catalog rejected the lookup: {catalogRejected.Reason}", CheckoutState.ReleasingBasket);
                await AdvanceOrderAsync(checkout.CheckoutId);
                break;


            default:
                return;
        }
    }

    /// <summary>
    /// Trin 1: Sender "PrepareBasketForCheckout" til Basket og venter. Svaret (BasketPrepared) gemmer
    /// varelinjerne og flytter til AwaitingCatalog.
    /// </summary>
    private async Task<bool> HandleAwaitingBasketAsync(CheckoutProcess checkout)
    {
        await SendPrepareBasketAsync(checkout);
        return false;
    }

    /// <summary>
    /// Sender "PrepareBasketForCheckout" til Basket og husker, hvilket svar vi venter på.
    /// </summary>
    /// <remarks>
    /// Gemmer først CorrelationId på checkouten og sender derefter. Går noget galt midt imellem,
    /// har vi et gemt id uden besked, ikke en besked uden id.
    /// </remarks>
    private async Task SendPrepareBasketAsync(CheckoutProcess checkout)
    {
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);
        await _repository.UpdateAsync(checkout);

        await _producer.SendAsync(
            CheckoutQueues.Basket,
            "PrepareBasketForCheckout",
            checkout.CheckoutId,
            correlationId,
            new PrepareBasketForCheckoutPayload { BasketId = checkout.BasketId, BuyerId = checkout.BuyerId });
    }

    /// <summary>
    /// Trin 2: Sender "GetCatalogItemsByIds" til Catalog og venter. Svaret (CatalogItemsReturned) gemmer navn,
    /// pris og billede på linjerne (snapshot) og flytter til AwaitingStockReservation. Mangler en vare, frigives kurven.
    /// </summary>
    private async Task<bool> HandleAwaitingCatalogAsync(CheckoutProcess checkout)
    {
        await SendGetCatalogItemsAsync(checkout);
        return false;
    }

    /// <summary>
    /// Sender "GetCatalogItemsByIds" til Catalog med de unikke varenumre fra kurven.
    /// </summary>
    private async Task SendGetCatalogItemsAsync(CheckoutProcess checkout)
    {
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);

        var ids = checkout.Lines.Select(l => l.CatalogItemId).Distinct().ToList();

        await _repository.UpdateAsync(checkout);

        await _producer.SendAsync(
            CheckoutQueues.Catalog,
            "GetCatalogItemsByIds",
            checkout.CheckoutId,
            correlationId,
            new GetCatalogItemsByIdsPayload { CatalogItemIds = ids });
    }

    /// <summary>
    /// Trin 3: Opretter en ventende ordre (PendingInventory), sender "ReserveStock" til Inventory og venter.
    /// Svaret (StockReserved) flytter til AwaitingStockConfirmation.
    /// </summary>
    /// <remarks>
    /// Ordren oprettes før reservationen, fordi Inventory skal bruge ordrens id. Afvises reservationen
    /// (StockReservationRejected), afvises ordren, og kun kurven frigives (intet lager er holdt).
    /// </remarks>
    private async Task<bool> HandleAwaitingStockReservationAsync(CheckoutProcess checkout)
    {
        var items = checkout.Lines
            .Select(l => new OrderItem(
                new CatalogItemOrdered(l.CatalogItemId, l.ProductName!, l.PictureUri!),
                l.UnitPrice!.Value,
                l.Quantity))
            .ToList();

        var order = new Order(checkout.BuyerId, checkout.ShipToAddress, items, checkout.CheckoutId);

        await _orderRepository.AddAsync(order);

        var stockLines = checkout.Lines
            .Select(l => new StockLineDto { CatalogItemId = l.CatalogItemId, Quantity = l.Quantity })
            .ToList();

        await SendReserveStockAsync(checkout, order.Id, stockLines);
        return false;
    }

    /// <summary>
    /// Sender "ReserveStock" til Inventory med ordrens id og varelinjerne.
    /// </summary>
    private async Task SendReserveStockAsync(CheckoutProcess checkout, int orderId, List<StockLineDto> stockLines)
    {
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);

        await _repository.UpdateAsync(checkout);

        await _producer.SendAsync(
            CheckoutQueues.Inventory,
            "ReserveStock",
            checkout.CheckoutId,
            correlationId,
            new ReserveStockPayload { OrderId = orderId, Items = stockLines });
    }

    /// <summary>
    /// Trin 4: Sender "ConfirmStockReservation" til Inventory og venter. Svaret (StockReservationConfirmed)
    /// bekræfter ordren og flytter til OrderConfirmedCleanupPending.
    /// </summary>
    /// <remarks>
    /// Kan Inventory ikke bekræfte (StockConfirmationRejected), afvises ordren, og lageret frigives først,
    /// derefter kurven.
    /// </remarks>
    private async Task<bool> HandleAwaitingStockConfirmationAsync(CheckoutProcess checkout)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkout.CheckoutId));

        if (order is null)
        {
            return false;
        }

        await SendConfirmStockAsync(checkout, order.Id);

        return false;
    }

    /// <summary>
    /// Sender "ConfirmStockReservation" til Inventory med ordrens id.
    /// </summary>
    private async Task SendConfirmStockAsync(CheckoutProcess checkout, int orderId)
    {
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);

        await _repository.UpdateAsync(checkout);

        await _producer.SendAsync(
            CheckoutQueues.Inventory,
            "ConfirmStockReservation",
            checkout.CheckoutId,
            correlationId,
            new ConfirmStockReservationPayload { OrderId = orderId });
    }

    /// <summary>
    /// Trin 5: Sender "CompleteBasketCheckout" til Basket (tøm den købte kurv) og venter. Svaret
    /// (BasketCheckoutCompleted) flytter til Completed.
    /// </summary>
    /// <remarks>
    /// Ordren er allerede bekræftet. Afvises eller udebliver svaret, bliver checkouten stående i
    /// OrderConfirmedCleanupPending og kan forsøges igen. Lageret frigives aldrig herfra.
    /// </remarks>
    private async Task<bool> HandleOrderConfirmedCleanupPendingAsync(CheckoutProcess checkout)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkout.CheckoutId));

        if (order is null)
        {
            return false;
        }

        await SendCompleteBasketAsync(checkout, order.Id);
        return false;
    }

    /// <summary>
    /// Sender "CompleteBasketCheckout" til Basket med kurvens id, køberen og ordrens id.
    /// </summary>
    private async Task SendCompleteBasketAsync(CheckoutProcess checkout, int orderId)
    {
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);

        await _repository.UpdateAsync(checkout);

        await _producer.SendAsync(
            CheckoutQueues.Basket,
            "CompleteBasketCheckout",
            checkout.CheckoutId,
            correlationId,
            new CompleteBasketCheckoutPayload { BasketId = checkout.BasketId, BuyerId = checkout.BuyerId, OrderId = orderId });
    }

    /// <summary>
    /// Fejl-sporet: Sender "ReleaseStockReservation" til Inventory og venter. Svaret (StockReservationReleased)
    /// flytter til ReleasingBasket. Udebliver svaret, bliver checkouten stående og kan forsøges igen.
    /// </summary>
    private async Task<bool> HandleReleasingStockAsync(CheckoutProcess checkout)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkout.CheckoutId));

        if (order is null)
        {
            return false;
        }

        await SendReleaseStockAsync(checkout, order.Id);
        return false;
    }

    /// <summary>
    /// Sender "ReleaseStockReservation" til Inventory med ordrens id og årsagen til, at checkouten fejlede.
    /// </summary>
    private async Task SendReleaseStockAsync(CheckoutProcess checkout, int orderId)
    {
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);

        await _repository.UpdateAsync(checkout);

        await _producer.SendAsync(
            CheckoutQueues.Inventory,
            "ReleaseStockReservation",
            checkout.CheckoutId,
            correlationId,
            new ReleaseStockReservationPayload { OrderId = orderId, Reason = checkout.FailureReason ?? string.Empty });
    }

    /// <summary>
    /// Fejl-sporet: Sender "ReleaseBasketCheckout" til Basket (indholdet bevares) og venter. Svaret
    /// (BasketCheckoutReleased) flytter til Failed. Udebliver svaret, bliver checkouten stående og kan forsøges igen.
    /// </summary>
    private async Task<bool> HandleReleasingBasketAsync(CheckoutProcess checkout)
    {
        await SendReleaseBasketAsync(checkout);
        return false;
    }

    /// <summary>
    /// Sender "ReleaseBasketCheckout" til Basket med kurvens id, køberen og årsagen til, at checkouten fejlede.
    /// </summary>
    private async Task SendReleaseBasketAsync(CheckoutProcess checkout)
    {
        var correlationId = Guid.NewGuid();
        checkout.ExpectReply(correlationId);

        await _repository.UpdateAsync(checkout);

        await _producer.SendAsync(
            CheckoutQueues.Basket,
            "ReleaseBasketCheckout",
            checkout.CheckoutId,
            correlationId,
            new ReleaseBasketCheckoutPayload { BasketId = checkout.BasketId, BuyerId = checkout.BuyerId, Reason = checkout.FailureReason ?? string.Empty });
    }

    /// <summary>
    /// Sender checkouten ind i fejl-sporet: gemmer årsagen og flytter til den første oprydningstilstand.
    /// </summary>
    /// <remarks>
    /// Returnerer true, så en handler kan bruge den direkte som sin returværdi.
    /// </remarks>
    private async Task<bool> FailAsync(CheckoutProcess checkout, string reason, CheckoutState nextState)
    {
        checkout.SetFailure(reason);
        checkout.MoveTo(nextState);
        await _repository.UpdateAsync(checkout);
        return true;
    }
}
