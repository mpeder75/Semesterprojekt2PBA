using BasketService.api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BasketService.api.Data.Queries
{
    public class BasketQueryService : IBasketQueryService
    {
        private readonly BasketContext _dbContext;

        public BasketQueryService(BasketContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<int> CountTotalBasketItems(string username)
        {
            var totalItems = await _dbContext.Baskets
            .Where(basket => basket.BuyerId == username)
            .SelectMany(item => item.Items)
            .SumAsync(sum => sum.Quantity);

            return totalItems;
        }
    }
}
