using ApiGateway.Extensions;
using ApiGateway;
using Microsoft.FeatureManagement;
using ApiGateway.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement();
builder.Services.AddSingleton<ServiceRoutingSelection>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddConfigFilter<ServiceRoutingFilter>();


builder.Services.AddRequestTimeouts();

builder.Services.AddGatewayRateLimiting(builder.Configuration);

builder.Services.AddGatewayForwardedHeaders(
    isAzureAppService: !string.IsNullOrEmpty(
        builder.Configuration["WEBSITE_SITE_NAME"]));

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(30);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

var app = builder.Build();

// Initialize before MapReverseProxy loads routes:
// ServiceRoutingFilter reads these selections.
var routingSelection =
    app.Services.GetRequiredService<ServiceRoutingSelection>();

await routingSelection.InitializeAsync();

app.UseForwardedHeaders();

if (app.Environment.IsProduction())
{
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    });
}

app.UseRouting();
app.UseRateLimiter();
app.UseRequestTimeouts();

app.UseMiddleware<ServiceToggleMiddleware>();
app.UseMiddleware<ProxyTimeoutMiddleware>();

app.MapGet("/health/gateway", () => Results.Ok(new { status = "Running" }));
app.MapReverseProxy();

app.Run();

public partial class Program { }
