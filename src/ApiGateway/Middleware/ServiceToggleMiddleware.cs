using Yarp.ReverseProxy.Model;

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
        var route = context.GetEndpoint()?.Metadata
            .GetMetadata<RouteModel>()?.Config;

        var service = ServiceRouteMetadata.GetService(route);

        if (service is not null && _routingSelection.IsDisabled(service))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: $"{service} API is disabled",
                detail: "Enable its gateway feature flag and restart the gateway.")
                .ExecuteAsync(context);

            return;
        }

        await _next(context);
    }
}
