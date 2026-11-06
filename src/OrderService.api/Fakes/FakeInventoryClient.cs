using OrderService.api.Interfaces;
using OrderService.api.Messages;

namespace OrderService.api.Fakes;

public class FakeInventoryClient : IInventoryClient
{
    public Task<ReserveStockResult> ReserveStock(Guid checkoutId, List<StockLineDto> itemsToReserve)
    {
        return Task.FromResult(new ReserveStockResult
        {
            IsSuccess = true,
            Reason = string.Empty
        });
    }
}
