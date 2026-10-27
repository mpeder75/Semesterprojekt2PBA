using ApiGateway.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

namespace ApiGateway.Tests;

public sealed class StartupValidationTests
{
    [Theory]
    [InlineData("catalog-service", "UseCatalogService")]
    [InlineData("basket-service", "UseBasketService")]
    [InlineData("order-service", "UseOrderService")]
    public void Enabled_service_requires_a_configured_destination(string cluster, string flag)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true,
            EmptyClusters = new[] { cluster }
        });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        Assert.Contains(flag, error.ToString());
        Assert.Contains($"{cluster} requires at least one destination", error.ToString());
    }

    [Theory]
    [InlineData("catalog-service", "relative/path")]
    [InlineData("catalog-service", "ftp://example.test/")]
    [InlineData("basket-service", "")]
    [InlineData("basket-service", "relative/path")]
    [InlineData("order-service", "ftp://example.test/")]
    [InlineData("order-service", "not a URL")]
    public void Invalid_selected_addresses_stop_startup(string cluster, string address)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true,
            Configuration = new Dictionary<string, string?>
            {
                [$"ReverseProxy:Clusters:{cluster}:Destinations:test:Address"] = address
            }
        });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        Assert.Contains($"{cluster} requires at least one destination", error.ToString());
    }

    [Fact]
    public void Web_destination_is_required_even_when_domain_services_are_disabled()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            EmptyClusters = new[] { "monolith-web" }
        });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        Assert.Contains("monolith-web requires at least one destination", error.ToString());
    }

    [Fact]
    public async Task Disabled_basket_and_order_do_not_require_destinations()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            EmptyClusters = new[] { "basket-service", "order-service" }
        });
        using var client = factory.CreateGatewayClient();
        await ServiceRoutingTests.AssertDisabled(client, "/api/baskets", "Basket");
        await ServiceRoutingTests.AssertDisabled(client, "/api/orders", "Order");
        Assert.Empty(factory.Requests);
    }

    [Fact]
    public void Catalog_off_requires_its_legacy_fallback()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            EmptyClusters = new[] { "monolith-api" }
        });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        Assert.Contains("Catalog fallback", error.ToString());
        Assert.Contains("monolith-api requires at least one destination", error.ToString());
    }

    [Fact]
    public void Actual_production_configuration_rejects_catalog_off()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Environment = "Production",
            UseConfiguredDestinations = true
        });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        Assert.Contains("Catalog fallback", error.ToString());
        Assert.Contains("monolith-api requires at least one destination", error.ToString());
    }

    [Fact]
    public void Unknown_service_metadata_stops_startup()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Configuration = new Dictionary<string, string?>
            {
                ["ReverseProxy:Routes:catalog-items:Metadata:Service"] = "UnknownService"
            }
        });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        Assert.Contains("Unknown gateway service 'UnknownService'", error.ToString());
    }

    [Fact]
    public async Task Missing_flags_have_the_same_behavior_as_false()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Configuration = new Dictionary<string, string?>
            {
                ["FeatureManagement:UseCatalogService"] = null,
                ["FeatureManagement:UseBasketService"] = null,
                ["FeatureManagement:UseOrderService"] = null
            }
        });
        using var client = factory.CreateGatewayClient();
        await ServiceRoutingTests.AssertForwarded(
            client, "/api/catalog-items", "monolith-api", "/api/catalog-items");
        await ServiceRoutingTests.AssertDisabled(client, "/api/baskets", "Basket");
        await ServiceRoutingTests.AssertDisabled(client, "/api/orders", "Order");
    }

    [Fact]
    public async Task Selection_must_be_initialized_once_before_it_can_be_used()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:monolith-web:Destinations:test:Address"] = "http://localhost/",
                ["ReverseProxy:Clusters:monolith-api:Destinations:test:Address"] = "http://localhost/"
            }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddFeatureManagement();
        services.AddSingleton<ServiceRoutingSelection>();
        using var provider = services.BuildServiceProvider();
        var selection = provider.GetRequiredService<ServiceRoutingSelection>();

        var earlyError = Assert.Throws<InvalidOperationException>(() => selection.GetClusterId("Catalog"));
        Assert.Contains("Initialize service routing", earlyError.Message);
        await selection.InitializeAsync();
        Assert.Equal("monolith-api", selection.GetClusterId("Catalog"));
        var repeatedError = await Assert.ThrowsAsync<InvalidOperationException>(selection.InitializeAsync);
        Assert.Contains("already been initialized", repeatedError.Message);
        Assert.Throws<InvalidOperationException>(() => selection.GetClusterId("UnknownService"));
    }
}
