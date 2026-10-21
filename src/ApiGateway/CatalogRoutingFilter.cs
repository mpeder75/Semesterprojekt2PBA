using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public sealed class CatalogRoutingFilter : IProxyConfigFilter
{
    private readonly CatalogRoutingSelection _selection;

    public CatalogRoutingFilter(CatalogRoutingSelection selection)
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
            ? route with { ClusterId = _selection.ClusterId }
            : route);
    }
}
