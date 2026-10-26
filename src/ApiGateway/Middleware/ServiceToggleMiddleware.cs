namespace ApiGateway.Middleware;

public sealed class ServiceToggleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ServiceRoutingSelection _routingSelection;

    public ServiceToggleMiddleware(
        RequestDelegate next,
        ServiceRoutingSelection routingSelection)
    {
        _next = next;
        _routingSelection = routingSelection;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string? disabledService = null;

        if (context.Request.Path.StartsWithSegments("/api/baskets")
            && !_routingSelection.UseBasketService)
        {
            disabledService = "Basket";
        }
        else if (context.Request.Path.StartsWithSegments("/api/orders")
            && !_routingSelection.UseOrderService)
        {
            disabledService = "Order";
        }

        if (disabledService is not null)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: $"{disabledService} API is disabled",
                detail: "Enable its gateway feature flag and restart the gateway.")
                .ExecuteAsync(context);

            return;
        }

        await _next(context);
    }
}
