using OrderService.api.Messages;

namespace OrderService.api.Interfaces;

public interface IInventoryClient
{
    public Task<ReserveStockResult> ReserveStock(Guid checkoutId, int orderId, List<StockLineDto> itemsToReserve);
    public Task<ConfirmStockReservationResult> ConfirmStockReservation(Guid checkoutId);
}
