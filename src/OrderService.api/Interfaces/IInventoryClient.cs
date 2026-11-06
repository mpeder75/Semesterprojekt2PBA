using OrderService.api.Messages;

namespace OrderService.api.Interfaces;

public interface IInventoryClient
{
    public Task<ReserveStockResult> ReserveStock(Guid checkoutId, List<StockLineDto> itemsToReserve);
}
