using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public sealed class ServiceRoutingFilter : IProxyConfigFilter
{
    private readonly ServiceRoutingSelection _selection;

    public ServiceRoutingFilter(ServiceRoutingSelection selection)
    {
        _selection = selection;
    }

    public ValueTask<ClusterConfig> ConfigureClusterAsync(
        ClusterConfig cluster, CancellationToken cancel)
        => ValueTask.FromResult(cluster);

    public ValueTask<RouteConfig> ConfigureRouteAsync(
        RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        var service = ServiceRouteMetadata.GetService(route);

        if (service is null)
        {
            return ValueTask.FromResult(route);
        }

        var clusterId = _selection.GetClusterId(service);

        // Disabled routes remain matchable so the middleware can return 503.
        return ValueTask.FromResult(clusterId is null
            ? route
            : route with { ClusterId = clusterId });
    }
}
