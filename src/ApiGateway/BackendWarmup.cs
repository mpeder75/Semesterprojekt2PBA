using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public sealed class BackendWarmup(
    IProxyConfigProvider proxyConfigProvider,
    ServiceRoutingSelection routingSelection,
    IHttpClientFactory httpClientFactory,
    ILogger<BackendWarmup> logger) : BackgroundService
{
    public const string ClientName = "BackendWarmup";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = proxyConfigProvider.GetConfig();
        var activeClusters = config.Routes
            .Select(route => ServiceRouteMetadata.GetService(route) is { } service
                ? routingSelection.GetClusterId(service)
                : route.ClusterId)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var client = httpClientFactory.CreateClient(ClientName);
        var warmups = new List<Task>();
        foreach (var cluster in config.Clusters)
        {
            if (!activeClusters.Contains(cluster.ClusterId) || cluster.Destinations is null)
            {
                continue;
            }

            foreach (var (destinationId, destination) in cluster.Destinations)
            {
                warmups.Add(WarmDestinationAsync(client, cluster.ClusterId, destinationId, destination.Address, stoppingToken));
            }
        }

        await Task.WhenAll(warmups);
    }

    private async Task WarmDestinationAsync(
        HttpClient client, string clusterId, string destinationId, string address, CancellationToken stoppingToken)
    {
        try
        {
            using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
            var statusCode = (int)response.StatusCode;
            logger.Log(statusCode >= 500 ? LogLevel.Warning : LogLevel.Information,
                "Startup warm-up for {ClusterId}/{DestinationId} returned HTTP {StatusCode}",
                clusterId, destinationId, statusCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Startup warm-up failed for {ClusterId}/{DestinationId}", clusterId, destinationId);
        }
    }
}
