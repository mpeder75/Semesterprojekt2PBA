using Ardalis.Specification;
using OrderService.api.Entities.OrderAggregate;

namespace OrderService.api.Specifications;

public class CheckoutProcessByCheckoutIdSpec : Specification<CheckoutProcess>
{
    public CheckoutProcessByCheckoutIdSpec(Guid checkoutId)
    {
        Query.Where(c => c.CheckoutId == checkoutId);
    }
}

