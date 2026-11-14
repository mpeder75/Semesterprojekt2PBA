using Ardalis.GuardClauses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Web.Configuration;
using Microsoft.eShopWeb.Web.Features.MyOrders;
using Microsoft.eShopWeb.Web.Features.OrderDetails;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Authorize] // Controllers that mainly require Authorization still use Controller/View; other pages use Pages
[Route("[controller]/[action]")]
public class OrderController : Controller
{
    private readonly IMediator _mediator;
    private readonly IOrderApiClient _orderApiClient;
    private readonly CheckoutServiceSettings _checkoutSettings;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        IMediator mediator,
        IOrderApiClient orderApiClient,
        IOptions<CheckoutServiceSettings> checkoutSettings,
        ILogger<OrderController> logger)
    {
        _mediator = mediator;
        _orderApiClient = orderApiClient;
        _checkoutSettings = checkoutSettings.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> MyOrders()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        // Når checkout går via OrderService, ligger ordrerne dér og ikke i monolittens database
        if (_checkoutSettings.UseOrderService)
        {
            try
            {
                return View(await _orderApiClient.GetOrdersAsync(User.Identity.Name));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Could not read orders from OrderService");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Your orders are temporarily unavailable. Please try again.");
            }
        }

        var viewModel = await _mediator.Send(new GetMyOrders(User.Identity.Name));

        return View(viewModel);
    }

    [HttpGet("{orderId}")]
    public async Task<IActionResult> Detail(int orderId)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        if (_checkoutSettings.UseOrderService)
        {
            try
            {
                var order = await _orderApiClient.GetOrderAsync(User.Identity.Name, orderId);

                return order is null ? BadRequest("No such order found for this user.") : View(order);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Could not read order {OrderId} from OrderService", orderId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Your order is temporarily unavailable. Please try again.");
            }
        }

        var viewModel = await _mediator.Send(new GetOrderDetails(User.Identity.Name, orderId));

        if (viewModel == null)
        {
            return BadRequest("No such order found for this user.");
        }

        return View(viewModel);
    }
}
