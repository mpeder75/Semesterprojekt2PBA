using Ardalis.Specification;
using OrderService.api.Entities.OrderAggregate;

namespace OrderService.api.Specifications;

public class OrderByCheckoutIdSpec : Specification<Order>
{
    public OrderByCheckoutIdSpec(Guid checkoutId)
    {
        Query.Where(o => o.CheckoutId == checkoutId);
    }
}
