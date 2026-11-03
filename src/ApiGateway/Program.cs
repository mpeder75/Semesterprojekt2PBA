using ApiGateway.Extensions;
using ApiGateway;
using Microsoft.FeatureManagement;
using ApiGateway.Middleware;
using ApiGateway.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GatewayCircuitBreakerOptions>()
    .Bind(builder.Configuration.GetRequiredSection(
        GatewayCircuitBreakerOptions.SectionName))
    .Validate(
        options => double.IsFinite(options.FailureRatio)
            && options.FailureRatio > 0
            && options.FailureRatio <= 1,
        "CircuitBreaker:FailureRatio must be greater than 0 and at most 1.")
    .Validate(
        options => options.MinimumThroughput >= 2,
        "CircuitBreaker:MinimumThroughput must be at least 2.")
    .Validate(
        options => options.SamplingDurationSeconds >= 1
            && options.SamplingDurationSeconds <= 86_400,
        "CircuitBreaker:SamplingDurationSeconds must be between 1 and 86400.")
    .Validate(
        options => options.BreakDurationSeconds >= 1
            && options.BreakDurationSeconds <= 86_400,
        "CircuitBreaker:BreakDurationSeconds must be between 1 and 86400.")
    .ValidateOnStart();

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<GatewayCircuitBreakerRegistry>();

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

app.UseMiddleware<ServiceToggleMiddleware>();
app.UseMiddleware<GatewayCircuitBreakerMiddleware>();
app.UseRequestTimeouts();
app.UseMiddleware<ProxyTimeoutMiddleware>();

app.MapGet("/health/gateway", () => Results.Ok(new { status = "Running" }));
app.MapReverseProxy();

app.Run();

public partial class Program { }
