using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messages;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderMessagePublisher _orderMessagePublisher;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IOrderMessagePublisher orderMessagePublisher)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _orderMessagePublisher = orderMessagePublisher;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket);
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        /*
        // Original eShopOnWeb: ordren blev bygget og gemt i CatalogDb. Nu sendes den som CreateOrderCommand.

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();
        */

        var createOrderCommand = new CreateOrderCommand
        {
            BuyerId = basket.BuyerId,
            ShipToAddress = new AddressMessage
            {
                Street = shippingAddress.Street,
                City = shippingAddress.City,
                State = shippingAddress.State,
                Country = shippingAddress.Country,
                ZipCode = shippingAddress.ZipCode
            },
            Items = basket.Items.Select(basketItem =>
            {
                var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
                return new CreateOrderItemMessage
                {
                    CatalogItemId = catalogItem.Id,
                    ProductName = catalogItem.Name,
                    UnitPrice = basketItem.UnitPrice,
                    Units = basketItem.Quantity,
                    PictureUrl = _uriComposer.ComposePicUri(catalogItem.PictureUri)
                };
            }).ToList()
        };
        
        await _orderMessagePublisher.PublishCreateOrderAsync(createOrderCommand);
        
        // await _orderRepository.AddAsync(order);
    }
}
