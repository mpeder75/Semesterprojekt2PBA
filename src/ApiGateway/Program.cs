using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddRequestTimeouts();

var apiPermitLimit = builder.Configuration.GetValue(
    "Gateway:RateLimiting:ApiPermitLimit", 60);

var loginPermitLimit = builder.Configuration.GetValue(
    "Gateway:RateLimiting:LoginPermitLimit", 10);

if (loginPermitLimit <= 0)
{
    throw new InvalidOperationException(
        "LoginPermitLimit must be greater than zero.");
}

if (apiPermitLimit <= 0)
{
    throw new InvalidOperationException(
        "ApiPermitLimit must be greater than zero.");
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("login", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

app.UseRouting();
app.UseRateLimiter();
app.UseRequestTimeouts();

app.Use(async (context, next) =>
{
    var timeoutToken = context.Features
        .Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken
        ?? CancellationToken.None;

    await next(context);

    var proxyError = context.Features.Get<IForwarderErrorFeature>();

    if (timeoutToken.IsCancellationRequested
        && proxyError?.Error is ForwarderError.RequestCanceled
            or ForwarderError.RequestBodyCanceled
        && !context.Response.HasStarted)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;

        app.Logger.LogWarning(
            "Gateway timeout for {Method} {Path}. TraceId: {TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);
    }
});

app.MapGet("/health/gateway", () => Results.Ok(new { status = "Running" }));
app.MapReverseProxy();

app.Run();

public partial class Program { }
