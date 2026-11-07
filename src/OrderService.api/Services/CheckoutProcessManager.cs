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

    public CheckoutProcessManager(IRepository<CheckoutProcess> repository, IBasketClient basketClient,
        ICatalogClient catalogClient, IInventoryClient inventoryClient)
    {
        _repository = repository;
        _basketClient = basketClient;
        _catalogClient = catalogClient;
        _inventoryClient = inventoryClient;
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
            return false;
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
        
        if(!result.IsSuccess)
        {
            return false;
        }

        // 3. opdater linjerne med snapshot
        foreach (var line in checkout.Lines)
        {
            var item = result.Items.FirstOrDefault(i => i.CatalogItemId == line.CatalogItemId);
            if (item is null)
            {
                return false;
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

        var stockLines = checkout.Lines.Select(l =>
            new StockLineDto { CatalogItemId = l.CatalogItemId, Quantity = l.Quantity }).ToList();


        var result = await _inventoryClient.ReserveStock(checkout.CheckoutId, stockLines);
        
        if (!result.IsSuccess)
        {
            return false;
        }

        checkout.MoveTo(CheckoutState.AwaitingStockConfirmation); 
        await _repository.UpdateAsync(checkout); 
        return true;
    }
}
