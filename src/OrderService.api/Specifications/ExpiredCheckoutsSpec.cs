using Ardalis.Specification;
using OrderService.api.Entities.OrderAggregate;

namespace OrderService.api.Specifications;

/// <summary>
/// Finder checkouts, hvis deadline er overskredet, og som endnu ikke er bekræftet (de fire ventetilstande før
/// bekræftelsen). Bekræftede, færdige og mislykkede checkouts og dem, der allerede rydder op, medtages ikke.
/// </summary>
public class ExpiredCheckoutsSpec : Specification<CheckoutProcess>
{
    public ExpiredCheckoutsSpec(DateTimeOffset now)
    {
        Query.Where(c => c.DeadlineAt != null
                         && c.DeadlineAt < now
                         && (c.State == CheckoutState.AwaitingBasket
                             || c.State == CheckoutState.AwaitingCatalog
                             || c.State == CheckoutState.AwaitingStockReservation
                             || c.State == CheckoutState.AwaitingStockConfirmation));
    }
}
