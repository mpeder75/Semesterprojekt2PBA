using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messages;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Features.MyOrders;

public class GetMyOrdersHandler : IRequestHandler<GetMyOrders, IEnumerable<OrderViewModel>>
{
    // private readonly IReadRepository<Order> _orderRepository;

    /*
    public GetMyOrdersHandler(IReadRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }
    */

    private readonly IRequestReplyClient _requestReplyClient;

    public GetMyOrdersHandler(IRequestReplyClient requestReplyClient)
    {
        _requestReplyClient = requestReplyClient;
    }

    /*
    public async Task<IEnumerable<OrderViewModel>> Handle(GetMyOrders request,
        CancellationToken cancellationToken)
    {
        var specification = new CustomerOrdersSpecification(request.UserName);
        var orders = await _orderRepository.ListAsync(specification, cancellationToken);

        return orders.Select(o => new OrderViewModel
        {
            OrderDate = o.OrderDate,
            OrderNumber = o.Id,
            ShippingAddress = o.ShipToAddress,
            Total = o.Total()
        });
    }
    */


    public async Task<IEnumerable<OrderViewModel>> Handle(GetMyOrders request, CancellationToken cancellationToken)
    {
        var reply = await _requestReplyClient.RequestAsync<ListOrdersRequest, ListOrdersResponse>(
            "order.list",
            new ListOrdersRequest { BuyerId = request.UserName },
            cancellationToken);

        return reply.Orders.Select(o => new OrderViewModel
        {
            OrderDate = o.OrderDate,
            OrderNumber = o.Id,
            ShippingAddress = new Address(o.Address.Street, o.Address.City, o.Address.State,
                o.Address.Country, o.Address.ZipCode),
            Total = o.Total
        });
    }
}
