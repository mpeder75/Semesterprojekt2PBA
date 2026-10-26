using System.Threading.RateLimiting;

namespace ApiGateway.Extensions;

public static class GatewayRateLimitingExtensions
{
    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var apiPermitLimit = configuration.GetValue(
            "Gateway:RateLimiting:ApiPermitLimit", 60);

        var loginPermitLimit = configuration.GetValue(
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

        services.AddRateLimiter(options =>
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

        return services;
    }
}
