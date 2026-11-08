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
/// Process Manager for checkout. Styrer flowet gennem tilstandene, gemmer fremdriften i databasen
/// og kalder Basket, Catalog og Inventory (og rydder op, hvis noget fejler).
/// </summary>
public class CheckoutProcessManager
{
    private readonly IBasketClient _basketClient;
    private readonly ICatalogClient _catalogClient;
    private readonly IRepository<CheckoutProcess> _repository;
    private readonly IInventoryClient _inventoryClient;
    private readonly IRepository<Order> _orderRepository;
    private readonly ICheckoutRequestProducer _producer;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CheckoutProcessManager(IRepository<CheckoutProcess> repository, IBasketClient basketClient,
        ICatalogClient catalogClient, IInventoryClient inventoryClient, IRepository<Order> orderRepository, ICheckoutRequestProducer producer)
    {
        _repository = repository;
        _basketClient = basketClient;
        _catalogClient = catalogClient;
        _inventoryClient = inventoryClient;
        _orderRepository = orderRepository;
        _producer = producer;
    }

    /// <summary>
    /// Finder checkouten og kører den videre, trin for trin, indtil et trin stopper eller flowet er færdigt.
    /// </summary>
    /// <remarks>
    /// Hver tilstand har sin egen handler. En handler returnerer true, når tilstanden er flyttet (så løkken
    /// fortsætter), og false, når flowet skal stoppe. Normal vej: AwaitingBasket til Completed.
    /// Fejl-sporet: ReleasingStock og ReleasingBasket. Completed og Failed er slutninger.
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
    /// Spørger Basket om kurven, gemmer varelinjerne og flytter til AwaitingCatalog.
    /// Afviser Basket, går checkouten direkte til Failed (der er intet at frigive).
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
    /// har vi et gemt id uden besked, ikke en besked uden id. Kaldes ikke endnu.
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
    /// Behandler et svar fra en anden service (kaldes af reply-consumeren).
    /// </summary>
    /// <remarks>
    /// Ignorerer svaret, hvis checkouten ikke findes, eller hvis svarets CorrelationId ikke er det, vi venter på
    /// (gammelt eller dobbelt svar). Derefter vælges handling efter MessageType.
    /// Mangler endnu: selve behandlingen af BasketPrepared.
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

                // Lageret er reserveret, men kunne ikke bekræftes: frigiv lageret først, derefter kurven
                await FailAsync(checkout, $"Stock confirmation failed: {stockConfirmationRejected.Reason}", CheckoutState.ReleasingStock);
                await AdvanceOrderAsync(checkout.CheckoutId);
                break;


            default:
                return;
        }
    }

    /// <summary>
    /// Spørger Catalog om varerne i kurven og gemmer navn, pris og billede på linjerne (snapshot).
    /// Flytter til AwaitingStockReservation. Fejler Catalog, eller mangler en vare, frigives kurven.
    /// </summary>
    private async Task<bool> HandleAwaitingCatalogAsync(CheckoutProcess checkout)
    {
        await SendGetCatalogItemsAsync(checkout);
        return false;
    }

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
    /// Opretter en ventende ordre (PendingInventory), reserverer lager hos Inventory og flytter til
    /// AwaitingStockConfirmation.
    /// </summary>
    /// <remarks>
    /// Ordren oprettes før reservationen, fordi Inventory skal bruge ordrens id. Afvises reservationen,
    /// afvises ordren, og kun kurven frigives (intet lager er holdt).
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
    /// Beder Inventory bekræfte reservationen, bekræfter ordren og flytter til OrderConfirmedCleanupPending.
    /// </summary>
    /// <remarks>
    /// Kan Inventory ikke bekræfte, afvises ordren, og lageret frigives først, derefter kurven.
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
    /// Beder Basket tømme den købte kurv og flytter til Completed.
    /// </summary>
    /// <remarks>
    /// Ordren er allerede bekræftet. Fejler oprydningen, bliver checkouten stående, så den kan forsøges igen
    /// senere. Lageret frigives aldrig herfra.
    /// </remarks>
    private async Task<bool> HandleOrderConfirmedCleanupPendingAsync(CheckoutProcess checkout)
    {
        var result = await _basketClient.CompleteBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);

        if (!result.IsSuccess)
        {
            return false;
        }

        checkout.MoveTo(CheckoutState.Completed);
        await _repository.UpdateAsync(checkout);
        return true;
    }

    /// <summary>
    /// Fejl-sporet: Beder Inventory frigive lageret og flytter til ReleasingBasket.
    /// Fejler frigivelsen, bliver checkouten stående i ReleasingStock, så den kan forsøges igen.
    /// </summary>
    private async Task<bool> HandleReleasingStockAsync(CheckoutProcess checkout)
    {
        var result = await _inventoryClient.ReleaseStockReservation(checkout.CheckoutId);

        if (!result.IsSuccess)
        {
            return false;
        }

        checkout.MoveTo(CheckoutState.ReleasingBasket);
        await _repository.UpdateAsync(checkout);
        return true;
    }

    /// <summary>
    /// Fejl-sporet: Beder Basket frigive kurven (indholdet bevares) og flytter til Failed.
    /// Fejler frigivelsen, bliver checkouten stående i ReleasingBasket, så den kan forsøges igen.
    /// </summary>
    private async Task<bool> HandleReleasingBasketAsync(CheckoutProcess checkout)
    {
        var result = await _basketClient.ReleaseBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);

        if (!result.IsSuccess)
        {
            return false;
        }

        checkout.MoveTo(CheckoutState.Failed);
        await _repository.UpdateAsync(checkout);
        return true;
    }


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
    /// Sender checkouten ind i fejl-sporet: gemmer årsagen og flytter til den første oprydningstilstand.
    /// </summary>
    /// <remarks>
    /// Returnerer true, så løkken i AdvanceOrderAsync fortsætter med oprydningen med det samme.
    /// </remarks>
    private async Task<bool> FailAsync(CheckoutProcess checkout, string reason, CheckoutState nextState)
    {
        checkout.SetFailure(reason);
        checkout.MoveTo(nextState);
        await _repository.UpdateAsync(checkout);
        return true;
    }
}
