using ApiGateway.Tests.Support;
using Microsoft.Extensions.Configuration;

namespace ApiGateway.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Shared_configuration_has_no_backend_addresses()
    {
        using var config = ReadConfiguration();
        Assert.Empty(config.GetSection("ReverseProxy:Clusters").GetChildren());
    }

    [Fact]
    public void Development_provides_all_local_backends()
    {
        using var config = ReadConfiguration("Development");
        foreach (var cluster in GatewayFactory.Clusters)
        {
            var destinations = config.GetSection($"ReverseProxy:Clusters:{cluster}:Destinations").GetChildren();
            var destination = Assert.Single(destinations);
            var address = new Uri(destination["Address"]!);
            Assert.True(address.IsLoopback, $"{cluster} must use a local development address.");
        }
    }

    [Fact]
    public void Production_has_real_https_backends_and_an_explicitly_unavailable_legacy_api()
    {
        using var config = ReadConfiguration("Production");
        Assert.True(config.GetValue<bool>("FeatureManagement:UseCatalogService"));
        Assert.Empty(config.GetSection("ReverseProxy:Clusters:monolith-api:Destinations").GetChildren());
        foreach (var cluster in GatewayFactory.Clusters.Where(cluster => cluster != "monolith-api"))
        {
            var destinations = config.GetSection($"ReverseProxy:Clusters:{cluster}:Destinations").GetChildren();
            Assert.NotEmpty(destinations);
            foreach (var destination in destinations)
            {
                var address = new Uri(destination["Address"]!);
                Assert.Equal(Uri.UriSchemeHttps, address.Scheme);
                Assert.False(address.IsLoopback, $"{cluster} must not point to localhost in Production.");
            }
        }
        var hosts = config["AllowedHosts"]!.Split(';');
        Assert.DoesNotContain("*", hosts);
        Assert.DoesNotContain("localhost", hosts);
        Assert.All(hosts, host => Assert.True(Uri.CheckHostName(host) == UriHostNameType.Dns));
    }

    [Theory]
    [InlineData("catalog-service", "Catalog")]
    [InlineData("basket-service", "Basket")]
    [InlineData("order-service", "Order")]
    public void Every_managed_route_declares_consistent_metadata_rate_policy_and_timeout(
        string cluster, string service)
    {
        using var config = ReadConfiguration();
        var routes = config.GetSection("ReverseProxy:Routes").GetChildren()
            .Where(route => route["ClusterId"] == cluster).ToArray();
        Assert.NotEmpty(routes);
        foreach (var route in routes)
        {
            Assert.Equal(service, route["Metadata:Service"]);
            Assert.Equal("api", route["RateLimiterPolicy"]);
            Assert.True(TimeSpan.TryParse(route["Timeout"], out var timeout) && timeout > TimeSpan.Zero,
                $"Route '{route.Key}' needs a positive timeout.");
        }
    }

    private static ConfigurationRoot ReadConfiguration(string? environment = null)
    {
        var builder = new ConfigurationBuilder().SetBasePath(GatewayFactory.ProjectRoot)
            .AddJsonFile("appsettings.json", optional: false);
        if (environment is not null)
            builder.AddJsonFile($"appsettings.{environment}.json", optional: false);
        return (ConfigurationRoot)builder.Build();
    }
}
