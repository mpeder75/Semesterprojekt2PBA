using System.Net;
using System.Text.Json;
using ApiGateway.Tests.Support;
using System.Text;
using ApiGateway.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.Tests;

public sealed class CircuitBreakerHttpTests
{
    [Theory]
    [InlineData("/api/catalog-items", "catalog-service", true)]
    [InlineData("/api/baskets", "basket-service", true)]
    [InlineData("/api/orders", "order-service", true)]
    [InlineData("/api/inventory/1", "inventory-service", true)]
    [InlineData("/api/catalog-items", "monolith-api", false)]
    public async Task Repeated_backend_failures_open_the_circuit_and_stop_forwarding(
        string path, string cluster, bool catalog)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = catalog,
            Basket = true,
            Order = true,
            Inventory = true,
            Configuration = BreakerSettings(),
            Backend = (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("simulated backend failure")
                })
        });
        using var client = factory.CreateGatewayClient();

        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(
                "simulated backend failure",
                await response.Content.ReadAsStringAsync());
            Assert.Equal(i + 1, factory.Requests.Count);
        }

        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(
                "application/problem+json",
                response.Content.Headers.ContentType?.MediaType);

            using var body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            Assert.Equal("circuit_open", body.RootElement.GetProperty("code").GetString());
            Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(
                body.RootElement.GetProperty("traceId").GetString()));
            Assert.True(response.Headers.RetryAfter?.Delta is { } delay
                && delay > TimeSpan.Zero);

            Assert.Equal(2, factory.Requests.Count);
        }

        Assert.All(factory.Requests, request => Assert.Equal(cluster, request.Cluster));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Catalog_routes_share_a_circuit_and_the_outage_stays_isolated(bool catalog)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = catalog,
            Basket = true,
            Order = true,
            Inventory = true,
            Configuration = BreakerSettings(),
            Backend = (request, _) => Task.FromResult(new HttpResponseMessage(
                request.Path.StartsWith("/api/catalog-", StringComparison.Ordinal)
                    ? HttpStatusCode.InternalServerError
                    : HttpStatusCode.OK))
        });
        using var client = factory.CreateGatewayClient();

        foreach (var path in new[] { "/api/catalog-items", "/api/catalog-brands" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        using var blocked = await client.GetAsync("/api/catalog-types");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        using var problem = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal("circuit_open", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, factory.Requests.Count);

        var healthyRoutes = new[]
        {
        ("/api/baskets", "basket-service", "/basket"),
        ("/api/orders", "order-service", "/api/orders"),
        ("/api/inventory/1", "inventory-service", "/api/inventory/1"),
        ("/", "monolith-web", "/"),
        ("/Identity/Account/Login", "monolith-web", "/Identity/Account/Login"),
        ("/api/unknown", "monolith-api", "/api/unknown")
    };

        foreach (var (path, cluster, backendPath) in healthyRoutes)
        {
            var before = factory.Requests.Count;
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(before + 1, factory.Requests.Count);
            var forwarded = factory.Requests.Last();
            Assert.Equal(cluster, forwarded.Cluster);
            Assert.Equal(backendPath, forwarded.Path);
        }

        var beforeHealth = factory.Requests.Count;
        using var health = await client.GetAsync("/health/gateway");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(beforeHealth, factory.Requests.Count);
    }

    [Theory]
    [InlineData("/api/catalog-items", "catalog-service", "/api/catalog-items")]
    [InlineData("/api/baskets/test-buyer/items", "basket-service", "/basket/test-buyer/items")]
    [InlineData("/api/orders", "order-service", "/api/orders")]
    [InlineData("/api/inventory/reservations", "inventory-service", "/api/inventory/reservations")]
    public async Task Failed_posts_are_forwarded_once_without_retries_or_fallback(
        string path, string cluster, string backendPath)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Basket = true,
            Order = true,
            Inventory = true,
            Configuration = BreakerSettings(),
            Backend = (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("write failed")
                })
        });
        using var client = factory.CreateGatewayClient();
        const string payload = "{\"test\":\"write\"}";

        for (var i = 0; i < 3; i++)
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(path, content);

            if (i < 2)
            {
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal("write failed", await response.Content.ReadAsStringAsync());
                Assert.Equal(i + 1, factory.Requests.Count);
            }
            else
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("circuit_open", body.RootElement.GetProperty("code").GetString());
                Assert.Equal(2, factory.Requests.Count);
            }
        }

        Assert.All(factory.Requests, request =>
        {
            Assert.Equal("POST", request.Method);
            Assert.Equal(cluster, request.Cluster);
            Assert.Equal(backendPath, request.Path);
            Assert.Equal(payload, request.Body);
        });
    }

    [Theory]
    [InlineData(true, "catalog-service")]
    [InlineData(false, "monolith-api")]
    public async Task Http_requests_use_the_selected_backend_circuit(bool catalog, string cluster)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = catalog,
            Configuration = BreakerSettings()
        });
        using var client = factory.CreateGatewayClient();
        var registry = factory.Services.GetRequiredService<GatewayCircuitBreakerRegistry>();

        for (var i = 0; i < 2; i++)
        {
            await registry.GetPipeline(cluster).ExecuteAsync(_ =>
                ValueTask.FromResult(new ProxyOutcome(true, "simulated backend failure")));
        }

        using var response = await client.GetAsync("/api/catalog-items");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("circuit_open", body.RootElement.GetProperty("code").GetString());
        Assert.Empty(factory.Requests);
    }
    private static Dictionary<string, string?> BreakerSettings()
        => new()
        {
            ["CircuitBreaker:FailureRatio"] = "1",
            ["CircuitBreaker:MinimumThroughput"] = "2",
            ["CircuitBreaker:SamplingDurationSeconds"] = "30",
            ["CircuitBreaker:BreakDurationSeconds"] = "60"
        };
}
