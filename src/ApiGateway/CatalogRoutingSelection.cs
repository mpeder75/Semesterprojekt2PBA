using Microsoft.FeatureManagement;

namespace ApiGateway;

public sealed class CatalogRoutingSelection
{
    private readonly IVariantFeatureManager _featureManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CatalogRoutingSelection> _logger;
    private string? _clusterId;

    public CatalogRoutingSelection(
        IVariantFeatureManager featureManager,
        IConfiguration configuration,
        ILogger<CatalogRoutingSelection> logger)
    {
        _featureManager = featureManager;
        _configuration = configuration;
        _logger = logger;
    }

    public string ClusterId => _clusterId
        ?? throw new InvalidOperationException(
            "Initialize Catalog routing before mapping the reverse proxy.");

    public async Task InitializeAsync()
    {
        if (_clusterId is not null)
        {
            throw new InvalidOperationException(
                "Catalog routing has already been initialized.");
        }

        var useCatalogService = await _featureManager.IsEnabledAsync(
            "UseCatalogService");

        if (useCatalogService)
        {
            var destinations = _configuration.GetSection(
                "ReverseProxy:Clusters:catalog-service:Destinations")
                .GetChildren().ToArray();

            if (destinations.Length == 0 || destinations.Any(destination =>
                !Uri.TryCreate(destination["Address"], UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttp
                    && address.Scheme != Uri.UriSchemeHttps)))
            {
                throw new InvalidOperationException(
                    "UseCatalogService is enabled, but catalog-service requires " +
                    "at least one destination with a valid HTTP or HTTPS address.");
            }
        }

        _clusterId = useCatalogService ? "catalog-service" : "monolith-api";

        _logger.LogInformation(
            "Catalog migration routing: {ClusterId} (UseCatalogService: {Enabled})",
            _clusterId,
            useCatalogService);
    }
}
