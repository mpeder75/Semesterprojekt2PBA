using Microsoft.FeatureManagement;

namespace ApiGateway;

public sealed class ServiceRoutingSelection
{
    private readonly IVariantFeatureManager _featureManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceRoutingSelection> _logger;
    private Selection? _selection;

    public ServiceRoutingSelection(
        IVariantFeatureManager featureManager,
        IConfiguration configuration,
        ILogger<ServiceRoutingSelection> logger)
    {
        _featureManager = featureManager;
        _configuration = configuration;
        _logger = logger;
    }

    private Selection Current => _selection
        ?? throw new InvalidOperationException(
            "Initialize service routing before mapping the reverse proxy.");

    public string CatalogClusterId => Current.CatalogClusterId;
    public bool UseBasketService => Current.UseBasketService;
    public bool UseOrderService => Current.UseOrderService;

    public async Task InitializeAsync()
    {
        if (_selection is not null)
        {
            throw new InvalidOperationException(
                "Service routing has already been initialized.");
        }

        var catalog = await _featureManager.IsEnabledAsync("UseCatalogService");
        var basket = await _featureManager.IsEnabledAsync("UseBasketService");
        var order = await _featureManager.IsEnabledAsync("UseOrderService");

        if (catalog) ValidateDestination("UseCatalogService", "catalog-service");
        if (basket) ValidateDestination("UseBasketService", "basket-service");
        if (order) ValidateDestination("UseOrderService", "order-service");

        _selection = new Selection(
            catalog ? "catalog-service" : "monolith-api",
            basket,
            order);

        _logger.LogInformation(
            "Service routing: Catalog={Catalog}; Basket={Basket}; Order={Order}",
            CatalogClusterId,
            basket ? "basket-service" : "disabled",
            order ? "order-service" : "disabled");
    }

    private void ValidateDestination(string flag, string clusterId)
    {
        var destinations = _configuration.GetSection(
                $"ReverseProxy:Clusters:{clusterId}:Destinations")
            .GetChildren().ToArray();

        if (destinations.Length == 0 || destinations.Any(destination =>
            !Uri.TryCreate(destination["Address"], UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp
                && address.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException(
                $"{flag} is enabled, but {clusterId} requires at least " +
                "one destination with a valid HTTP or HTTPS address.");
        }
    }

    private sealed record Selection(
        string CatalogClusterId,
        bool UseBasketService,
        bool UseOrderService);
}
