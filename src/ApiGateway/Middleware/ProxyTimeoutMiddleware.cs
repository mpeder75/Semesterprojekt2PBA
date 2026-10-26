using Microsoft.AspNetCore.Http.Timeouts;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Middleware;

public sealed class ProxyTimeoutMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProxyTimeoutMiddleware> _logger;

    public ProxyTimeoutMiddleware(
        RequestDelegate next,
        ILogger<ProxyTimeoutMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var timeoutToken = context.Features
            .Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken
            ?? CancellationToken.None;

        await _next(context);

        var proxyError = context.Features.Get<IForwarderErrorFeature>();

        if (timeoutToken.IsCancellationRequested
            && proxyError?.Error is ForwarderError.RequestCanceled
                or ForwarderError.RequestBodyCanceled
            && !context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;

            _logger.LogWarning(
                "Gateway timeout for {Method} {Path}. TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);
        }
    }
}
