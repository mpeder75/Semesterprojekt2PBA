using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiGateway.Tests.Support;

namespace ApiGateway.Tests;

public sealed class ServiceRoutingTests
{
    public static IEnumerable<object[]> FlagCombinations()
    {
        foreach (var catalog in new[] { false, true })
        foreach (var basket in new[] { false, true })
        foreach (var order in new[] { false, true })
            yield return new object[] { catalog, basket, order };
    }

    [Theory]
    [MemberData(nameof(FlagCombinations))]
    public async Task Every_service_follows_its_own_flag(
        bool catalog, bool basket, bool order)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = catalog, Basket = basket, Order = order
        });
        using var client = factory.CreateGatewayClient();

        foreach (var path in new[] { "/api/catalog-items", "/api/catalog-brands", "/api/catalog-types" })
        {
            await AssertForwarded(client, path,
                catalog ? "catalog-service" : "monolith-api", path);
        }

        if (basket)
            await AssertForwarded(client, "/api/baskets", "basket-service", "/basket");
        else
            await AssertDisabled(client, "/api/baskets", "Basket");

        if (order)
            await AssertForwarded(client, "/api/orders", "order-service", "/api/orders");
        else
            await AssertDisabled(client, "/api/orders", "Order");

        Assert.Equal(3 + (basket ? 1 : 0) + (order ? 1 : 0), factory.Requests.Count);
    }

    [Theory]
    [InlineData("/api/catalog-items", "catalog-service", "/api/catalog-items")]
    [InlineData("/api/catalog-items/", "catalog-service", "/api/catalog-items/")]
    [InlineData("/api/catalog-items/123", "catalog-service", "/api/catalog-items/123")]
    [InlineData("/api/catalog-brands", "catalog-service", "/api/catalog-brands")]
    [InlineData("/api/catalog-types", "catalog-service", "/api/catalog-types")]
    [InlineData("/api/orders", "order-service", "/api/orders")]
    [InlineData("/api/orders/", "order-service", "/api/orders/")]
    [InlineData("/api/orders/123/items", "order-service", "/api/orders/123/items")]
    [InlineData("/api/baskets", "basket-service", "/basket")]
    [InlineData("/api/baskets/", "basket-service", "/basket/")]
    [InlineData("/api/baskets/customer/items", "basket-service", "/basket/customer/items")]
    public async Task Root_trailing_slash_and_nested_paths_reach_the_correct_backend(
        string path, string cluster, string backendPath)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true
        });
        using var client = factory.CreateGatewayClient();
        await AssertForwarded(client, path, cluster, backendPath);
        Assert.Single(factory.Requests);
    }

    [Theory]
    [InlineData("/api/orders", "Order")]
    [InlineData("/api/orders/", "Order")]
    [InlineData("/api/orders/123", "Order")]
    [InlineData("/api/baskets", "Basket")]
    [InlineData("/api/baskets/", "Basket")]
    [InlineData("/api/baskets/example", "Basket")]
    public async Task Disabled_services_return_problem_details_without_calling_a_backend(
        string path, string service)
    {
        using var factory = new GatewayFactory(new GatewayOptions { Catalog = true });
        using var client = factory.CreateGatewayClient();
        await AssertDisabled(client, path, service);
        Assert.Empty(factory.Requests);
    }

    [Theory]
    [InlineData("/", "monolith-web")]
    [InlineData("/Basket", "monolith-web")]
    [InlineData("/Order/MyOrders", "monolith-web")]
    [InlineData("/Identity/Account/Login", "monolith-web")]
    [InlineData("/User", "monolith-web")]
    [InlineData("/api/unknown", "monolith-api")]
    [InlineData("/api/orders-extra", "monolith-api")]
    [InlineData("/api/baskets-extra", "monolith-api")]
    [InlineData("/api/authenticate", "monolith-api")]
    public async Task Untagged_routes_keep_their_normal_destinations(string path, string cluster)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateGatewayClient();
        await AssertForwarded(client, path, cluster, path);
    }

    [Theory]
    [InlineData("Catalog", false, "monolith-api")]
    [InlineData("Catalog", true, "catalog-service")]
    [InlineData("Basket", false, null)]
    [InlineData("Basket", true, "basket-service")]
    [InlineData("Order", false, null)]
    [InlineData("Order", true, "order-service")]
    public async Task Service_metadata_controls_a_route_with_an_unrelated_id_and_path(
        string service, bool enabled, string? expectedCluster)
    {
        const string path = "/a-completely-different-prefix/example";
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = service == "Catalog" && enabled,
            Basket = service == "Basket" && enabled,
            Order = service == "Order" && enabled,
            Configuration = new Dictionary<string, string?>
            {
                ["ReverseProxy:Routes:unrelated-name:ClusterId"] = "monolith-web",
                ["ReverseProxy:Routes:unrelated-name:Metadata:Service"] = service,
                ["ReverseProxy:Routes:unrelated-name:Match:Path"] = path,
                ["ReverseProxy:Routes:unrelated-name:Order"] = "-50"
            }
        });
        using var client = factory.CreateGatewayClient();
        if (expectedCluster is null)
        {
            await AssertDisabled(client, path, service);
            Assert.Empty(factory.Requests);
        }
        else
        {
            await AssertForwarded(client, path, expectedCluster, path);
        }
    }

    [Theory]
    [InlineData("Catalog", "catalog-items", "/api/catalog-items", "catalog-service")]
    [InlineData("Basket", "basket-api", "/api/baskets", "basket-service")]
    [InlineData("Order", "order-api", "/api/orders", "order-service")]
    public async Task Service_labels_are_case_insensitive(
        string service, string route, string path, string cluster)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true,
            Configuration = new Dictionary<string, string?>
            {
                [$"ReverseProxy:Routes:{route}:Metadata:Service"] = service.ToLowerInvariant()
            }
        });
        using var client = factory.CreateGatewayClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        Assert.Equal(cluster, forwarded!.Cluster);
    }

    [Theory]
    [InlineData("Catalog", "/api/catalog-items/7?include=details&page=2", "/api/catalog-items/7")]
    [InlineData("Basket", "/api/baskets/customer/items?include=details&page=2", "/basket/customer/items")]
    [InlineData("Order", "/api/orders/7?include=details&page=2", "/api/orders/7")]
    public async Task Forwarding_preserves_method_query_body_and_authorization(
        string service, string path, string expectedPath)
    {
        const string json = "{\"itemId\":7,\"quantity\":2}";
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true
        });
        using var client = factory.CreateGatewayClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        Assert.Equal(service.ToLowerInvariant() + "-service", forwarded!.Cluster);
        Assert.Equal("PUT", forwarded.Method);
        Assert.Equal(expectedPath, forwarded.Path);
        Assert.Equal("?include=details&page=2", forwarded.Query);
        Assert.Equal(json, forwarded.Body);
        Assert.Equal("Bearer test-token", forwarded.Authorization);
    }

    [Theory]
    [InlineData("/api/authenticate", "monolith-api")]
    [InlineData("/Identity/Account/Login", "monolith-web")]
    public async Task Login_posts_reach_their_own_backend(string path, string cluster)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateGatewayClient();
        using var response = await client.PostAsync(path, new StringContent("test"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        Assert.Equal(cluster, forwarded!.Cluster);
        Assert.Equal("POST", forwarded.Method);
    }

    [Fact]
    public async Task Health_is_answered_by_the_gateway_without_a_backend_call()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateGatewayClient();
        using var response = await client.GetAsync("/health/gateway");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Running", body.RootElement.GetProperty("status").GetString());
        Assert.Empty(factory.Requests);
    }

    internal static async Task AssertForwarded(
        HttpClient client, string path, string cluster, string backendPath)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        Assert.Equal(cluster, forwarded!.Cluster);
        Assert.Equal(backendPath, forwarded.Path);
    }

    internal static async Task AssertDisabled(HttpClient client, string path, string service)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"{service} API is disabled", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());
    }
}
