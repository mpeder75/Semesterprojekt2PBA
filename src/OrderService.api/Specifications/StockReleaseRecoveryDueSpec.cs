using Ardalis.Specification;
using OrderService.api.Entities.OrderAggregate;

namespace OrderService.api.Specifications;

public sealed class StockReleaseRecoveryDueSpec : Specification<CheckoutProcess>
{
    public StockReleaseRecoveryDueSpec(DateTimeOffset now)
    {
        Query.Where(checkout => checkout.State == CheckoutState.ReleasingStock
            && (checkout.StockRecoveryDueAt == null || checkout.StockRecoveryDueAt <= now));
    }
}
