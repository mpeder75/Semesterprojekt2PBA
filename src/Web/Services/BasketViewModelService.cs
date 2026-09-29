using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Pages.Basket;

namespace Microsoft.eShopWeb.Web.Services;

public class BasketViewModelService : IBasketViewModelService
{
    private readonly IRepository<Basket> _basketRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IBasketQueryService _basketQueryService;
    private readonly ICatalogClient _catalogClient;

    public BasketViewModelService(IRepository<Basket> basketRepository,
        ICatalogClient catalogClient,
        IUriComposer uriComposer,
        IBasketQueryService basketQueryService)
    {
        _basketRepository = basketRepository;
        _uriComposer = uriComposer;
        _basketQueryService = basketQueryService;
        _catalogClient = catalogClient;
    }

    public async Task<BasketViewModel> GetOrCreateBasketForUser(string userName)
    {
        var basketSpec = new BasketWithItemsSpecification(userName);
        var basket = (await _basketRepository.FirstOrDefaultAsync(basketSpec));

        if (basket == null)
        {
            return await CreateBasketForUser(userName);
        }
        var viewModel = await Map(basket);
        return viewModel;
    }

    private async Task<BasketViewModel> CreateBasketForUser(string userId)
    {
        var basket = new Basket(userId);
        await _basketRepository.AddAsync(basket);

        return new BasketViewModel()
        {
            BuyerId = basket.BuyerId,
            Id = basket.Id,
        };
    }

    private async Task<List<BasketItemViewModel>> GetBasketItems(IReadOnlyCollection<BasketItem> basketItems)
    {
        var catalogItems = await _catalogClient.GetItemsByIdsAsync(basketItems.Select(b => b.CatalogItemId).ToArray());

        var items = basketItems.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);

            var basketItemViewModel = new BasketItemViewModel
            {
                Id = basketItem.Id,
                UnitPrice = basketItem.UnitPrice,
                Quantity = basketItem.Quantity,
                CatalogItemId = basketItem.CatalogItemId,
                PictureUrl = _uriComposer.ComposePicUri(catalogItem.PictureUri),
                ProductName = catalogItem.Name
            };
            return basketItemViewModel;
        }).ToList();

        return items;
    }

    public async Task<BasketViewModel> Map(Basket basket)
    {
        return new BasketViewModel()
        {
            BuyerId = basket.BuyerId,
            Id = basket.Id,
            Items = await GetBasketItems(basket.Items)
        };
    }

    public async Task<int> CountTotalBasketItems(string username)
    {
        var counter = await _basketQueryService.CountTotalBasketItems(username);

        return counter;
    }
}
