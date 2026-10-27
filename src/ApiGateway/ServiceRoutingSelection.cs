using Microsoft.FeatureManagement;

namespace ApiGateway;

public sealed class ServiceRoutingSelection
{
    private static readonly ServiceDefinition[] Definitions =
    {
        new(ServiceRouteMetadata.Catalog, "UseCatalogService",
            "catalog-service", "monolith-api"),

        new(ServiceRouteMetadata.Basket, "UseBasketService",
            "basket-service", null),

        new(ServiceRouteMetadata.Order, "UseOrderService",
            "order-service", null)
    };

    private readonly IVariantFeatureManager _featureManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceRoutingSelection> _logger;

    private IReadOnlyDictionary<string, string?>? _clusterIds;

    public ServiceRoutingSelection(
        IVariantFeatureManager featureManager,
        IConfiguration configuration,
        ILogger<ServiceRoutingSelection> logger)
    {
        _featureManager = featureManager;
        _configuration = configuration;
        _logger = logger;
    }

    public string? GetClusterId(string service)
    {
        var clusterIds = _clusterIds
            ?? throw new InvalidOperationException(
                "Initialize service routing before mapping the reverse proxy.");

        if (!clusterIds.TryGetValue(service, out var clusterId))
        {
            throw new InvalidOperationException(
                $"Unknown gateway service '{service}'.");
        }

        return clusterId;
    }

    public bool IsDisabled(string service)
        => GetClusterId(service) is null;

    public async Task InitializeAsync()
    {
        if (_clusterIds is not null)
        {
            throw new InvalidOperationException(
                "Service routing has already been initialized.");
        }

        ValidateDestination("Web routing", "monolith-web");

        var clusterIds = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            var enabled = await _featureManager.IsEnabledAsync(
                definition.FeatureFlag);

            var clusterId = enabled
                ? definition.PrimaryClusterId
                : definition.FallbackClusterId;

            if (clusterId is not null)
            {
                ValidateDestination(
                    enabled
                        ? definition.FeatureFlag
                        : $"{definition.Service} fallback",
                    clusterId);
            }

            clusterIds.Add(definition.Service, clusterId);
        }

        _clusterIds = clusterIds;

        foreach (var (service, clusterId) in clusterIds)
        {
            _logger.LogInformation(
                "Service routing: {Service}={Destination}",
                service,
                clusterId ?? "disabled");
        }
    }

    private void ValidateDestination(string purpose, string clusterId)
    {
        var destinations = _configuration.GetSection(
                $"ReverseProxy:Clusters:{clusterId}:Destinations")
            .GetChildren().ToArray();

        if (destinations.Length == 0 || destinations.Any(destination =>
            !Uri.TryCreate(
                destination["Address"], UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp
                && address.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException(
                $"{purpose} is enabled, but {clusterId} requires at least " +
                "one destination with a valid HTTP or HTTPS address.");
        }
    }

    private sealed record ServiceDefinition(
        string Service,
        string FeatureFlag,
        string PrimaryClusterId,
        string? FallbackClusterId);
}
