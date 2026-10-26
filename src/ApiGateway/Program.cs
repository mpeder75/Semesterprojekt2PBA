using ApiGateway.Extensions;
using ApiGateway;
using Microsoft.FeatureManagement;
using ApiGateway.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement();
builder.Services.AddSingleton<ServiceRoutingSelection>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddConfigFilter<CatalogRoutingFilter>();


builder.Services.AddRequestTimeouts();

builder.Services.AddGatewayRateLimiting(builder.Configuration);

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
