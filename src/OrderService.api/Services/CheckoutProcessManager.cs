using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Messages;
using OrderService.api.Specifications;

namespace OrderService.api.Services;

public class CheckoutProcessManager
{
    private readonly IBasketClient _basketClient;
    private readonly ICatalogClient _catalogClient;
    private readonly IRepository<CheckoutProcess> _repository;
    private readonly IInventoryClient _inventoryClient;
    private readonly IRepository<Order> _orderRepository;

    public CheckoutProcessManager(IRepository<CheckoutProcess> repository, IBasketClient basketClient,
        ICatalogClient catalogClient, IInventoryClient inventoryClient, IRepository<Order> orderRepository)
    {
        _repository = repository;
        _basketClient = basketClient;
        _catalogClient = catalogClient;
        _inventoryClient = inventoryClient;
        _orderRepository = orderRepository;
    }

    public async Task AdvanceOrderAsync(Guid checkoutId)
    {
        // Find rækken
        var checkout = await _repository.FirstOrDefaultAsync(new CheckoutProcessByCheckoutIdSpec(checkoutId));

        if (checkout is null)
        {
            return;
        }

        while (true)
        {
            switch (checkout.State)
            {
                // ---------- Normal vej ----------
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

                // ---------- Fejl-sporet (kompensation) ----------
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

                // Completed og Failed er slutninger; flowet er færdigt
                default:
                    return;
            }
        }
    }

    private async Task<bool> HandleAwaitingBasketAsync(CheckoutProcess checkout)
    {
        // Spørg basket
        var result = await _basketClient.PrepareBasketForCheckoutAsync(checkout.BasketId, checkout.BuyerId);

        if (!result.IsSuccess)
        {
            // Basket låste ikke kurven, så der er intet at frigive: direkte til Failed
            return await FailAsync(checkout, $"Basket rejected the checkout: {result.Reason}", CheckoutState.Failed);
        }

        // Gem varelinjerne
        var lines = result.Items.Select(i => new CheckoutLine(i.CatalogItemId, i.Quantity));
        checkout.SetLines(lines);

        // opdater tilstanden
        checkout.MoveTo(CheckoutState.AwaitingCatalog);

        // Gem
        await _repository.UpdateAsync(checkout);
        return true;
    }

    private async Task<bool> HandleAwaitingCatalogAsync(CheckoutProcess checkout)
    {
        // Find id gemme linjer
        var ids = checkout.Lines.Select(l => l.CatalogItemId).Distinct().ToList();

        // 2. spørg catalog
        var result = await _catalogClient.GetCatalogItemsByIdsAsync(ids);

        if (!result.IsSuccess)
        {
            // Kurven er låst, men intet lager er reserveret: frigiv kurven
            return await FailAsync(checkout, $"Catalog lookup failed: {result.Reason}", CheckoutState.ReleasingBasket);
        }

        // 3. opdater linjerne med snapshot
        foreach (var line in checkout.Lines)
        {
            var item = result.Items.FirstOrDefault(i => i.CatalogItemId == line.CatalogItemId);
            if (item is null)
            {
                return await FailAsync(checkout, $"Catalog does not know item {line.CatalogItemId}.", CheckoutState.ReleasingBasket);
            }
            line.SetCatalogSnapshot(item.Name, item.Price, item.PictureUri);
        }

        // 4. opdater tilstanden
        checkout.MoveTo(CheckoutState.AwaitingStockReservation);

        await _repository.UpdateAsync(checkout);
        return true;
    }

    private async Task<bool> HandleAwaitingStockReservationAsync(CheckoutProcess checkout)
    {
        // Lav checkout-linjerne om til ordrelinjer (snapshot fra Catalog)
        var items = checkout.Lines
            .Select(l => new OrderItem(
                new CatalogItemOrdered(l.CatalogItemId, l.ProductName!, l.PictureUri!),
                l.UnitPrice!.Value,
                l.Quantity))
            .ToList();

        var order = new Order(checkout.BuyerId, checkout.ShipToAddress, items, checkout.CheckoutId);

        // Gem den ventende ordre (status PendingInventory), før vi reserverer lager
        await _orderRepository.AddAsync(order);

        // Reservér lager
        var stockLines = checkout.Lines
            .Select(l => new StockLineDto { CatalogItemId = l.CatalogItemId, Quantity = l.Quantity })
            .ToList();

        var result = await _inventoryClient.ReserveStock(checkout.CheckoutId, order.Id, stockLines);

        if (!result.IsSuccess)
        {
            // Reservationen er alt-eller-intet, så intet lager er holdt: afvis ordren og frigiv kun kurven
            order.Reject();
            await _orderRepository.UpdateAsync(order);

            return await FailAsync(checkout, $"Stock reservation failed: {result.Reason}", CheckoutState.ReleasingBasket);
        }

        // Opdater tilstanden og gem
        checkout.MoveTo(CheckoutState.AwaitingStockConfirmation);
        await _repository.UpdateAsync(checkout);

        return true;
    }

    private async Task<bool> HandleAwaitingStockConfirmationAsync(CheckoutProcess checkout)
    {
        // 1. spørg inventory
        var result = await _inventoryClient.ConfirmStockReservation(checkout.CheckoutId);

        // 2. find ordren
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkout.CheckoutId));

        if (!result.IsSuccess)
        {
            // Lageret er reserveret, men kunne ikke bekræftes: afvis ordren, frigiv lager og derefter kurv
            if (order is not null)
            {
                order.Reject();
                await _orderRepository.UpdateAsync(order);
            }

            return await FailAsync(checkout, $"Stock confirmation failed: {result.Reason}", CheckoutState.ReleasingStock);
        }

        if (order is null)
        {
            return false;
        }

        // 3. bekræft ordren
        order.Confirm();

        // 4. opdater ordren
        await _orderRepository.UpdateAsync(order);

        // 5. opdater tilstanden
        checkout.MoveTo(CheckoutState.OrderConfirmedCleanupPending);

        // 6. opdater checkout-processen
        await _repository.UpdateAsync(checkout);

        return true;
    }

    private async Task<bool> HandleOrderConfirmedCleanupPendingAsync(CheckoutProcess checkout)
    {
        // 1. Bed Basket om at tømme den købte kurv
        var result = await _basketClient.CompleteBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);

        if (!result.IsSuccess)
        {
            // Ordren er allerede bekræftet; oprydningen kan forsøges igen senere
            return false;
        }

        // 2. Checkouten er færdig
        checkout.MoveTo(CheckoutState.Completed);
        await _repository.UpdateAsync(checkout);
        return true;
    }

    private async Task<bool> HandleReleasingStockAsync(CheckoutProcess checkout)
    {
        var result = await _inventoryClient.ReleaseStockReservation(checkout.CheckoutId);

        if (!result.IsSuccess)
        {
            // Lageret er stadig holdt. Bliv i ReleasingStock, så det kan forsøges igen
            return false;
        }

        checkout.MoveTo(CheckoutState.ReleasingBasket);
        await _repository.UpdateAsync(checkout);
        return true;
    }

    private async Task<bool> HandleReleasingBasketAsync(CheckoutProcess checkout)
    {
        var result = await _basketClient.ReleaseBasketCheckoutAsync(checkout.BasketId, checkout.CheckoutId);

        if (!result.IsSuccess)
        {
            // Kurven er stadig låst. Bliv i ReleasingBasket, så det kan forsøges igen
            return false;
        }

        checkout.MoveTo(CheckoutState.Failed);
        await _repository.UpdateAsync(checkout);
        return true;
    }

    // Sender checkouten ind i fejl-sporet: gemmer årsagen og flytter til den første oprydningstilstand.
    // Returnerer true, så løkken i AdvanceOrderAsync fortsætter med oprydningen med det samme.
    private async Task<bool> FailAsync(CheckoutProcess checkout, string reason, CheckoutState nextState)
    {
        checkout.SetFailure(reason);
        checkout.MoveTo(nextState);
        await _repository.UpdateAsync(checkout);
        return true;
    }
}
