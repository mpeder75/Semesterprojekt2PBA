using OrderService.api.Interfaces;
using OrderService.api.Messages;

namespace OrderService.api.Fakes;

public class FakeInventoryClient : IInventoryClient
{
    public Task<ReserveStockResult> ReserveStock(Guid checkoutId, int orderId, List<StockLineDto> itemsToReserve)
    {
        return Task.FromResult(new ReserveStockResult
        {
            IsSuccess = true,
            Reason = string.Empty
        });
    }

    public Task<ConfirmStockReservationResult> ConfirmStockReservation(Guid checkoutId)
    {
        return Task.FromResult(new ConfirmStockReservationResult
        {
            IsSuccess = true,
            Reason = string.Empty
        });
    }

    public Task<ReleaseStockReservationResult> ReleaseStockReservation(Guid checkoutId)
    {
        return Task.FromResult(new ReleaseStockReservationResult
        {
            IsSuccess = true,
            Reason = string.Empty
        });
    }
}
