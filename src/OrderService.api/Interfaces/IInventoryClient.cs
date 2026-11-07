using OrderService.api.Messages.Inventory;

namespace OrderService.api.Interfaces;

public interface IInventoryClient
{
    public Task<ReserveStockResult> ReserveStock(Guid checkoutId, int orderId, List<StockLineDto> itemsToReserve);
    public Task<ConfirmStockReservationResult> ConfirmStockReservation(Guid checkoutId);
    public Task<ReleaseStockReservationResult> ReleaseStockReservation(Guid checkoutId);
}
