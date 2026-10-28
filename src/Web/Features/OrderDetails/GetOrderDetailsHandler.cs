using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messages;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Features.OrderDetails;

public class GetOrderDetailsHandler : IRequestHandler<GetOrderDetails, OrderDetailViewModel?>
{
    // private readonly IReadRepository<Order> _requestReplyClient;

    private readonly IRequestReplyClient _requestReplyClient;

    /*
    public GetOrderDetailsHandler(IReadRepository<Order> orderRepository)
    {
        _requestReplyClient = orderRepository;
    }
    */

    public GetOrderDetailsHandler(IRequestReplyClient requestReplyClient)
    {
        _requestReplyClient = requestReplyClient;
    }

    public async Task<OrderDetailViewModel?> Handle(GetOrderDetails request,
        CancellationToken cancellationToken)
    {
        /*
        var spec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = await _requestReplyClient.FirstOrDefaultAsync(spec, cancellationToken);

        if (order == null)
        {
            return null;
        }
        */

        var reply = await _requestReplyClient.RequestAsync<GetOrderRequest, GetOrderResponse>(
            "order.get",
            new GetOrderRequest { OrderId = request.OrderId },
            cancellationToken);

        if (!reply.Found)
        {
            return null;
        }

        return new OrderDetailViewModel
        {
            OrderDate = reply.OrderDate,
            OrderItems = reply.Items.Select(i => new OrderItemViewModel
            {
                PictureUrl = i.PictureUrl,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            OrderNumber = reply.Id,
            ShippingAddress = new Address(reply.Address.Street, reply.Address.City, reply.Address.State, reply.Address.Country, reply.Address.ZipCode),
            Total = reply.Total
        };
    }
}
