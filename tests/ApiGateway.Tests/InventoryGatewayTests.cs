using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiGateway.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace ApiGateway.Tests;

public class InventoryGatewayTests
{
    [Theory]
    [InlineData("POST", "/api/inventory", "{\"catalogItemId\":1,\"quantity\":10}")]
    [InlineData("GET", "/api/inventory/1", null)]
    [InlineData("PUT", "/api/inventory/1", "{\"quantity\":7}")]
    [InlineData("POST", "/api/inventory/1/adjustments", "{\"quantityChange\":-2}")]
    [InlineData("DELETE", "/api/inventory/1", null)]
    [InlineData("POST", "/api/inventory/reservations", "{\"orderId\":42,\"items\":[{\"catalogItemId\":1,\"quantity\":3}]}")]
    [InlineData("GET", "/api/inventory/reservations/42", null)]
    [InlineData("POST", "/api/inventory/reservations/42/release", null)]
    public async Task Every_inventory_operation_preserves_its_http_contract(string method, string path, string? json)
    {
        using var factory = new GatewayFactory(new GatewayOptions { Inventory = true });
        using var client = factory.CreateGatewayClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path + "?source=gateway");
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        Assert.NotNull(forwarded);
        Assert.Equal("inventory-service", forwarded.Cluster);
        Assert.Equal(method, forwarded.Method);
        Assert.Equal(path, forwarded.Path);
        Assert.Equal("?source=gateway", forwarded.Query);
        Assert.Equal(json, forwarded.Body);
        Assert.Equal("Bearer test-token", forwarded.Authorization);
        Assert.Single(factory.Requests);
    }

    [Fact]
    public void Production_requires_an_inventory_address_before_enabling_it()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Environment = "Production", Catalog = true, Inventory = true, UseConfiguredDestinations = true
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());

        Assert.Contains("UseInventoryService", error.ToString());
        Assert.Contains("inventory-service requires at least one destination", error.ToString());
    }

    [Fact]
    public async Task Production_uses_the_configured_inventory_host()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Environment = "Production", Catalog = true, Inventory = true, UseConfiguredDestinations = true,
            Configuration = new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:inventory-service:Destinations:azure:Address"] = "https://inventory.example.test/"
            }
        });
        using var client = factory.CreateGatewayClient(https: true);

        using var response = await client.GetAsync("/api/inventory/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        Assert.Equal("inventory-service", forwarded!.Cluster);
        Assert.Equal("inventory.example.test", forwarded.Host);
        Assert.Equal(client.BaseAddress!.Host, forwarded.ForwardedHost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inventory_outage_is_isolated_and_forwarding_recovers(bool connectionFailure)
    {
        var clock = new FakeTimeProvider();
        var healthy = false;
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true, Inventory = true,
            TimeProvider = clock,
            Configuration = new Dictionary<string, string?>
            {
                ["CircuitBreaker:FailureRatio"] = "1",
                ["CircuitBreaker:MinimumThroughput"] = "2",
                ["CircuitBreaker:BreakDurationSeconds"] = "10"
            },
            Backend = (request, _) => request.Cluster == "inventory-service" && !healthy
                ? connectionFailure
                    ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Inventory is unavailable"))
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
        });
        using var client = factory.CreateGatewayClient();

        foreach (var path in new[] { "/api/inventory/1", "/api/inventory/reservations/42" })
        {
            using var failed = await client.GetAsync(path);
            Assert.Equal(connectionFailure ? HttpStatusCode.BadGateway : HttpStatusCode.InternalServerError, failed.StatusCode);
        }

        using var blocked = await client.PostAsync("/api/inventory/reservations/42/release", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        using var problem = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal("circuit_open", problem.RootElement.GetProperty("code").GetString());
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(2, factory.Requests.Count);

        foreach (var path in new[] { "/api/catalog-items", "/api/baskets", "/api/orders" })
        {
            using var unaffected = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
        }
        Assert.Equal(5, factory.Requests.Count);

        healthy = true;
        clock.Advance(TimeSpan.FromSeconds(11));
        using var recovered = await client.GetAsync("/api/inventory/1");
        using var next = await client.GetAsync("/api/inventory/reservations/42");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(7, factory.Requests.Count);
        Assert.Equal(4, factory.Requests.Count(request => request.Cluster == "inventory-service"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task Inventory_business_errors_do_not_open_the_circuit(int status)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Inventory = true,
            Configuration = new Dictionary<string, string?>
            {
                ["CircuitBreaker:FailureRatio"] = "1",
                ["CircuitBreaker:MinimumThroughput"] = "2"
            },
            Backend = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent("inventory business result")
            })
        });
        using var client = factory.CreateGatewayClient();

        for (var i = 0; i < 6; i++)
        {
            using var response = await client.GetAsync("/api/inventory/1");
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal("inventory business result", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(6, factory.Requests.Count);
    }
}
