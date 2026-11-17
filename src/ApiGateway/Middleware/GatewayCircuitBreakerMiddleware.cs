using System.Globalization;
using ApiGateway.Resilience;
using Polly.CircuitBreaker;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace ApiGateway.Middleware;

public sealed class GatewayCircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GatewayCircuitBreakerRegistry _registry;
    private readonly ILogger<GatewayCircuitBreakerMiddleware> _logger;

    public GatewayCircuitBreakerMiddleware(
        RequestDelegate next,
        GatewayCircuitBreakerRegistry registry,
        ILogger<GatewayCircuitBreakerMiddleware> logger)
    {
        _next = next;
        _registry = registry;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var route = context.GetEndpoint()?.Metadata
            .GetMetadata<RouteModel>()?.Config;
        var service = ServiceRouteMetadata.GetService(route);
        var clusterId = route?.ClusterId;

        if (service is null || string.IsNullOrWhiteSpace(clusterId))
        {
            await ForwardAsync(context, clusterId);
            return;
        }

        var clientCancellation = context.RequestAborted;
        var forwardingStarted = false;
        var pipeline = _registry.GetPipeline(clusterId);

        try
        {
            await pipeline.ExecuteAsync(async _ =>
            {
                forwardingStarted = true;
                var error = await ForwardAsync(context, clusterId);

                return ProxyOutcomeClassifier.Classify(
                    context.Response.StatusCode,
                    error,
                    clientCancellation.IsCancellationRequested);
            }, cancellationToken: CancellationToken.None);
        }
        catch (BrokenCircuitException exception)
            when (!forwardingStarted && !context.Response.HasStarted)
        {
            if (clientCancellation.IsCancellationRequested)
            {
                return;
            }

            if (exception.RetryAfter is { } delay)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
                context.Response.Headers["Retry-After"] =
                    seconds.ToString(CultureInfo.InvariantCulture);
            }

            await Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Backend temporarily unavailable",
                detail: "This service is temporarily unavailable. Please try again later.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "circuit_open",
                    ["traceId"] = context.TraceIdentifier
                }).ExecuteAsync(context);
        }
    }

    private async Task<ForwarderError> ForwardAsync(HttpContext context, string? clusterId)
    {
        await _next(context);

        var error = context.Features.Get<IForwarderErrorFeature>()?.Error ?? ForwarderError.None;
        if (!string.IsNullOrWhiteSpace(clusterId)
            && error == ForwarderError.None
            && context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError("Backend {ClusterId} returned HTTP {StatusCode} for request {TraceId}",
                clusterId, context.Response.StatusCode, context.TraceIdentifier);
        }

        return error;
    }
}
