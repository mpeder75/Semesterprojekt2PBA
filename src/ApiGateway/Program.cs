using System.Threading.RateLimiting;
using ApiGateway;
using Microsoft.FeatureManagement;
using ApiGateway.MiddlewareExt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement();
builder.Services.AddSingleton<ServiceRoutingSelection>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddConfigFilter<CatalogRoutingFilter>();


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

var routingSelection = app.Services.GetRequiredService<ServiceRoutingSelection>();
await routingSelection.InitializeAsync();

app.UseRouting();
app.UseRateLimiter();
app.UseRequestTimeouts();

app.UseMiddleware<ServiceToggleMiddleware>();
app.UseMiddleware<ProxyTimeoutMiddleware>();

app.MapGet("/health/gateway", () => Results.Ok(new { status = "Running" }));
app.MapReverseProxy();

app.Run();

public partial class Program { }
