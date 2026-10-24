using Ardalis.Specification;
using BasketService.api.Entities.BasketAggregate;

namespace BasketService.api.Specifications
{
    public class BasketWithItemsSpecification : Specification<Basket>
    {
        public BasketWithItemsSpecification(int basketId) 
        {
            Query.
                Where(b => b.id == basketId)
                .Include(b => b.Items);
        }

        public BasketWithItemsSpecification(string buyerId)
        {
            Query
                .Where(b => b.BuyerId == buyerId)
                .Include (b => b.Items);
        }
    }
}
