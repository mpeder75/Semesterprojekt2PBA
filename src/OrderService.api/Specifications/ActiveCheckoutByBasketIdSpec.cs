using Ardalis.Specification;
using OrderService.api.Entities.OrderAggregate;

namespace OrderService.api.Specifications;

/// <summary>
/// Finder en checkout på kurven, der stadig er i gang (hverken Completed eller Failed).
/// </summary>
public class ActiveCheckoutByBasketIdSpec : Specification<CheckoutProcess>
{
    public ActiveCheckoutByBasketIdSpec(int basketId)
    {
        Query.Where(c => c.BasketId == basketId
                         && c.State != CheckoutState.Completed
                         && c.State != CheckoutState.Failed);
    }
}
