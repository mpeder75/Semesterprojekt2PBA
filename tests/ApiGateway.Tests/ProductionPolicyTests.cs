using System.Net;
using System.Net.Http.Json;
using ApiGateway.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.Tests;

public sealed class ProductionPolicyTests
{
    [Theory]
    [InlineData("/health/gateway")]
    [InlineData("/api/catalog-items")]
    public async Task Production_accepts_its_configured_gateway_hostname(string path)
    {
        using var factory = Production();
        using var client = factory.CreateGatewayClient(https: true);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("unapproved.example")]
    public async Task Production_rejects_unapproved_hosts_even_with_a_forwarded_host(string host)
    {
        using var factory = Production();
        var allowed = factory.Services.GetRequiredService<IConfiguration>()["AllowedHosts"]!;
        using var client = factory.CreateGatewayClient(https: true, host: host);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/gateway");
        request.Headers.Add("X-Forwarded-Host", allowed);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Requests);
    }

    [Theory]
    [InlineData("Testing", false, false)]
    [InlineData("Testing", true, false)]
    [InlineData("Development", true, false)]
    [InlineData("Production", false, false)]
    [InlineData("Production", true, true)]
    public async Task Hsts_is_sent_only_for_https_in_production(
        string environment, bool https, bool expected)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Environment = environment
        });
        using var client = factory.CreateGatewayClient(https);
        using var response = await client.GetAsync("/health/gateway");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, response.Headers.Contains("Strict-Transport-Security"));
        if (expected)
            Assert.Equal("max-age=2592000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Hsts_uses_the_https_scheme_from_a_trusted_proxy()
    {
        using var factory = Production();
        using var client = factory.CreateGatewayClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/gateway");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("max-age=2592000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Theory]
    [InlineData("/api/unknown", false)]
    [InlineData("/api/authenticate", true)]
    public async Task Unavailable_legacy_api_routes_return_503_without_forwarding(string path, bool post)
    {
        using var factory = Production();
        using var client = factory.CreateGatewayClient(https: true);
        using var response = post
            ? await client.PostAsync(path, new StringContent("test"))
            : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(factory.Requests);
    }

    [Theory]
    [InlineData("/", "monolith-web", "/")]
    [InlineData("/api/catalog-items", "catalog-service", "/api/catalog-items")]
    [InlineData("/api/baskets/customer/items", "basket-service", "/basket/customer/items")]
    [InlineData("/api/orders", "order-service", "/api/orders")]
    public async Task Production_uses_the_backend_host_and_preserves_the_public_forwarded_host(
        string path, string cluster, string backendPath)
    {
        using var factory = Production(allServices: true);
        using var client = factory.CreateGatewayClient(https: true);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        var address = factory.Services.GetRequiredService<IConfiguration>()[
            $"ReverseProxy:Clusters:{cluster}:Destinations:azure:Address"]!;
        Assert.Equal(cluster, forwarded!.Cluster);
        Assert.Equal(new Uri(address).Authority, forwarded.Host);
        Assert.Equal(backendPath, forwarded.Path);
        Assert.Equal(client.BaseAddress!.Authority, forwarded.ForwardedHost);
        Assert.Equal("https", forwarded.ForwardedProto);
    }

    private static GatewayFactory Production(bool allServices = false) => new(new GatewayOptions
    {
        Catalog = true,
        Basket = allServices,
        Order = allServices,
        Environment = "Production",
        UseConfiguredDestinations = true
    });
}
