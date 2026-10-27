using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public static class ServiceRouteMetadata
{
    public const string Catalog = "Catalog";
    public const string Basket = "Basket";
    public const string Order = "Order";

    public static string? GetService(RouteConfig? route)
    {
        var metadata = route?.Metadata;

        if (metadata is null
            || !metadata.TryGetValue("Service", out var service))
        {
            return null;
        }

        return service;
    }
}
