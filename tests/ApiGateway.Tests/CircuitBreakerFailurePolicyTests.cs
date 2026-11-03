using System.Net;
using System.Text.Json;
using ApiGateway.Tests.Support;
using ApiGateway.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.Tests;

public sealed class CircuitBreakerFailurePolicyTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(429)]
    public async Task Ordinary_client_error_responses_do_not_open_the_circuit(
        int status)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = BreakerSettings(),
            Backend = (_, _) => Task.FromResult(
                new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent("backend client error")
                })
        });
        using var client = factory.CreateGatewayClient();

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/api/catalog-items");

            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(
                "backend client error",
                await response.Content.ReadAsStringAsync());
            Assert.Equal(i + 1, factory.Requests.Count);
        }
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Selected_backend_server_errors_open_the_circuit(
        int status)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = BreakerSettings(),
            Backend = (_, _) => Task.FromResult(
                new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent("backend server error")
                })
        });
        using var client = factory.CreateGatewayClient();

        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync("/api/catalog-items");

            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(
                "backend server error",
                await response.Content.ReadAsStringAsync());
            Assert.Equal(i + 1, factory.Requests.Count);
        }

        await AssertCircuitOpenAsync(client, factory);
    }

    [Fact]
    public async Task Backend_connection_failures_open_the_circuit()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = BreakerSettings(),
            Backend = (_, _) => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Simulated connection failure"))
        });
        using var client = factory.CreateGatewayClient();

        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync("/api/catalog-items");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal(i + 1, factory.Requests.Count);
        }

        await AssertCircuitOpenAsync(client, factory);
    }

    [Fact]
    public async Task Gateway_timeouts_open_the_circuit()
    {
        var settings = BreakerSettings();
        settings["ReverseProxy:Routes:catalog-items:Timeout"] = "00:00:00.200";
        var canceledCalls = 0;

        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = settings,
            Backend = async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref canceledCalls);
                    throw;
                }
            }
        });
        using var client = factory.CreateGatewayClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync("/api/catalog-items");

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Equal(i + 1, factory.Requests.Count);
            Assert.Equal(i + 1, Volatile.Read(ref canceledCalls));
        }

        await AssertCircuitOpenAsync(client, factory);
        Assert.Equal(2, Volatile.Read(ref canceledCalls));
    }

    [Fact]
    public async Task Client_cancellation_does_not_open_the_circuit()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var backendCalls = 0;
        var completedCalls = 0;

        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = BreakerSettings(),
            RequestCompleted = () =>
            {
                if (Interlocked.Increment(ref completedCalls) == 2)
                {
                    completed.TrySetResult();
                }
            },
            Backend = async (_, token) =>
            {
                var call = Interlocked.Increment(ref backendCalls);

                if (call == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                if (call > 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }

                entered.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    stopped.TrySetResult();
                    throw;
                }
            }
        });
        using var client = factory.CreateGatewayClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        using (var failure = await client.GetAsync("/api/catalog-items"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        }

        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync("/api/catalog-items", cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await request);

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(stopped.Task.IsCompletedSuccessfully);
        Assert.Equal(2, factory.Requests.Count);

        using var healthy = await client.GetAsync("/api/catalog-items");

        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        Assert.Equal(3, factory.Requests.Count);
    }

    [Fact]
    public async Task Gateway_rate_limit_rejections_do_not_affect_the_circuit()
    {
        var settings = BreakerSettings();
        settings["CircuitBreaker:FailureRatio"] = "0.5";
        settings["Gateway:RateLimiting:ApiPermitLimit"] = "1";

        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = settings,
            Backend = (request, _) => Task.FromResult(new HttpResponseMessage(
                request.Query == "?fail=true"
                    ? HttpStatusCode.InternalServerError
                    : HttpStatusCode.OK))
        });
        using var limitedClient = factory.CreateGatewayClient();
        limitedClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");

        using (var healthy = await limitedClient.GetAsync("/api/catalog-items"))
        {
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        }

        for (var i = 0; i < 3; i++)
        {
            using var rejected = await limitedClient.GetAsync("/api/catalog-items");

            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Single(factory.Requests);
        }

        using var failingClient = factory.CreateGatewayClient();
        failingClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.20");

        using (var failure = await failingClient.GetAsync("/api/catalog-items?fail=true"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
            Assert.Equal(2, factory.Requests.Count);
        }

        using var nextClient = factory.CreateGatewayClient();
        nextClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.30");

        await AssertCircuitOpenAsync(nextClient, factory);
    }

    [Theory]
    [InlineData("/api/baskets", "Basket", "basket-service")]
    [InlineData("/api/orders", "Order", "order-service")]
    public async Task Disabled_service_responses_do_not_open_backend_circuits(
        string path, string service, string cluster)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Basket = false,
            Order = false,
            Configuration = BreakerSettings()
        });
        using var client = factory.CreateGatewayClient();

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            using var body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            Assert.Equal(
                $"{service} API is disabled",
                body.RootElement.GetProperty("title").GetString());

            Assert.False(body.RootElement.TryGetProperty("code", out _));
        }

        Assert.Empty(factory.Requests);

        var registry = factory.Services
            .GetRequiredService<GatewayCircuitBreakerRegistry>();

        var outcome = await registry.GetPipeline(cluster).ExecuteAsync(_ =>
            ValueTask.FromResult(new ProxyOutcome(false, "test_probe")));

        Assert.False(outcome.IsBackendFailure);
    }

    private static async Task AssertCircuitOpenAsync(
        HttpClient client, GatewayFactory factory)
    {
        using var response = await client.GetAsync("/api/catalog-items");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "circuit_open",
            body.RootElement.GetProperty("code").GetString());

        Assert.Equal(2, factory.Requests.Count);
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
