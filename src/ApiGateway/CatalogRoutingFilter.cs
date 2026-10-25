using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public sealed class CatalogRoutingFilter : IProxyConfigFilter
{
    private readonly ServiceRoutingSelection _selection;

    public CatalogRoutingFilter(ServiceRoutingSelection selection)
    {
        _selection = selection;
    }

    public ValueTask<ClusterConfig> ConfigureClusterAsync(
        ClusterConfig cluster, CancellationToken cancel)
        => ValueTask.FromResult(cluster);

    public ValueTask<RouteConfig> ConfigureRouteAsync(
        RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        var isCatalogRoute = route.RouteId is
            "catalog-items" or "catalog-brands" or "catalog-types";

        return ValueTask.FromResult(isCatalogRoute
                 ? route with { ClusterId = _selection.CatalogClusterId }
                 : route);
    }
}
