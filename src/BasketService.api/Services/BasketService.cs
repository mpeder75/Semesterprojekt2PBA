using Ardalis.GuardClauses;
using Ardalis.Result;
using BasketService.api.Entities.BasketAggregate;
using BasketService.api.Interfaces;
using BasketService.api.Specifications;

namespace BasketService.api.Services
{
    public class BasketService : IBasketService
    {
        private readonly IRepository<Basket> _basketRepository;
        private readonly IAppLogger<BasketService> _logger;

        public BasketService(IRepository<Basket> basketRepository, 
            IAppLogger<BasketService> logger)
        {
            _basketRepository = basketRepository;
            _logger = logger;
        }

        public async Task<Basket> AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1)
        {
            var basketSpec = new BasketWithItemsSpecification(username);
            var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

            if (basket == null)
            {                
                basket = new Basket(username);
                await _basketRepository.AddAsync(basket);
            }

            basket.AddItem(catalogItemId, price, quantity);

            await _basketRepository.UpdateAsync(basket);
            return basket;
        }

        public async Task DeleteBasketAsync(int basketId)
        {
            var basket = await _basketRepository.GetByIdAsync(basketId);
            Guard.Against.Null(basket, nameof(basket));
            await _basketRepository.DeleteAsync(basket);
        }

        public async Task<Result<Basket>> SetQuantities(int basketId, Dictionary<string, int> quantities)
        {
            var basketSpec = new BasketWithItemsSpecification(basketId);
            var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);
            if (basket == null) return Result<Basket>.NotFound();

            foreach (var item in basket.Items)
            {
                if (quantities.TryGetValue(item.id.ToString(), out var quantity))
                {
                    if (_logger != null) _logger.LogInformation($"Updating quantity of item ID:{item.id} to {quantity}.");
                    item.SetQuantity(quantity);
                }
            }
            basket.RemoveEmptyItems();
            await _basketRepository.UpdateAsync(basket);
            return basket;
        }

        public async Task TransferBasketAsync(string anonmousId, string userName)
        {
            var anonymousBasketSpec = new BasketWithItemsSpecification(anonmousId);
            var anonymousBasket = await _basketRepository.FirstOrDefaultAsync(anonymousBasketSpec);
            if (anonymousBasket == null) return;
            var userBasketSpec = new BasketWithItemsSpecification(userName);
            var userBasket = await _basketRepository.FirstOrDefaultAsync(userBasketSpec);
            if(userBasket == null)
            {
                userBasket = new Basket(userName);
                await _basketRepository.AddAsync(userBasket);
            }
            foreach (var item in anonymousBasket.Items)
            {
                userBasket.AddItem(item.CatalogItemId, item.UnitPrice, item.Quantity);
            }
            await _basketRepository.UpdateAsync(userBasket);
            await _basketRepository.DeleteAsync(anonymousBasket);
        }
    }
}
