using ApiGateway.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Microsoft.Extensions.Time.Testing;

namespace ApiGateway.Tests;

public sealed class GatewayCircuitBreakerRegistryTests
{
    private static readonly ProxyOutcome Success = new(false, "not_backend_failure");
    private static readonly ProxyOutcome Failure = new(true, "backend_server_error");

    [Fact]
    public void The_same_backend_reuses_its_pipeline()
    {
        using var registry = CreateRegistry();

        Assert.Same(
            registry.GetPipeline("catalog-service"),
            registry.GetPipeline("catalog-service"));
    }

    [Fact]
    public async Task Concurrent_first_access_returns_one_shared_pipeline()
    {
        using var registry = CreateRegistry();

        var pipelines = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(
                () => registry.GetPipeline("catalog-service"))));

        Assert.All(pipelines, pipeline => Assert.Same(pipelines[0], pipeline));
    }

    [Fact]
    public async Task Failure_history_is_retained_and_open_circuit_skips_the_callback()
    {
        using var registry = CreateRegistry();
        await OpenCatalogCircuit(registry);
        var callbackCalls = 0;

        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() =>
            registry.GetPipeline("catalog-service").ExecuteAsync(_ =>
            {
                callbackCalls++;
                return ValueTask.FromResult(Success);
            }).AsTask());

        Assert.Equal(0, callbackCalls);
    }

    [Theory]
    [InlineData("basket-service")]
    [InlineData("order-service")]
    [InlineData("monolith-api")]
    public async Task An_open_catalog_circuit_does_not_block_other_backends(string clusterId)
    {
        using var registry = CreateRegistry();
        await OpenCatalogCircuit(registry);

        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() =>
            registry.GetPipeline("catalog-service")
                .ExecuteAsync(_ => ValueTask.FromResult(Success)).AsTask());

        var outcome = await registry.GetPipeline(clusterId)
            .ExecuteAsync(_ => ValueTask.FromResult(Success));

        Assert.Equal(Success, outcome);
    }

    [Fact]
    public async Task Separate_registries_do_not_share_failure_history()
    {
        using var first = CreateRegistry();
        using var second = CreateRegistry();
        await OpenCatalogCircuit(first);

        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() =>
            first.GetPipeline("catalog-service")
                .ExecuteAsync(_ => ValueTask.FromResult(Success)).AsTask());

        var outcome = await second.GetPipeline("catalog-service")
            .ExecuteAsync(_ => ValueTask.FromResult(Success));

        Assert.Equal(Success, outcome);
    }

    [Fact]
    public async Task Results_not_marked_as_backend_failures_do_not_open_the_circuit()
    {
        using var registry = CreateRegistry();

        for (var i = 0; i < 5; i++)
        {
            var outcome = await registry.GetPipeline("catalog-service")
                .ExecuteAsync(_ => ValueTask.FromResult(Success));

            Assert.Equal(Success, outcome);
        }
    }

    [Fact]
    public async Task Local_exceptions_do_not_open_the_backend_circuit()
    {
        using var registry = CreateRegistry();
        var pipeline = registry.GetPipeline("catalog-service");

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.ExecuteAsync(_ => ValueTask.FromException<ProxyOutcome>(
                    new InvalidOperationException("Simulated gateway error."))).AsTask());
        }

        var outcome = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(Success));

        Assert.Equal(Success, outcome);
    }

    [Fact]
    public async Task The_configured_minimum_call_count_is_required_before_opening()
    {
        using var registry = CreateRegistry(
            new GatewayCircuitBreakerOptions
            {
                FailureRatio = 1,
                MinimumThroughput = 3
            },
            new FakeTimeProvider());
        var pipeline = registry.GetPipeline("catalog-service");

        for (var i = 0; i < 3; i++)
        {
            var outcome = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(Failure));
            Assert.Equal(Failure, outcome);
        }

        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() =>
            pipeline.ExecuteAsync(_ => ValueTask.FromResult(Success)).AsTask());
    }

    [Fact]
    public async Task The_circuit_opens_when_the_configured_failure_ratio_is_reached()
    {
        using var registry = CreateRegistry(
            new GatewayCircuitBreakerOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 4
            },
            new FakeTimeProvider());
        var pipeline = registry.GetPipeline("catalog-service");

        for (var i = 0; i < 3; i++)
        {
            var outcome = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(Success));
            Assert.Equal(Success, outcome);
        }

        for (var i = 0; i < 3; i++)
        {
            var outcome = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(Failure));
            Assert.Equal(Failure, outcome);
        }

        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() =>
            pipeline.ExecuteAsync(_ => ValueTask.FromResult(Success)).AsTask());
    }

    [Fact]
    public async Task Failures_outside_the_configured_sampling_window_expire()
    {
        var clock = new FakeTimeProvider();
        using var registry = CreateRegistry(
            new GatewayCircuitBreakerOptions
            {
                FailureRatio = 1,
                MinimumThroughput = 2,
                SamplingDurationSeconds = 10
            },
            clock);
        var pipeline = registry.GetPipeline("catalog-service");

        var first = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(Failure));
        Assert.Equal(Failure, first);
        clock.Advance(TimeSpan.FromSeconds(11));

        for (var i = 0; i < 2; i++)
        {
            var outcome = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(Failure));
            Assert.Equal(Failure, outcome);
        }

        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() =>
            pipeline.ExecuteAsync(_ => ValueTask.FromResult(Success)).AsTask());
    }

    private static async Task OpenCatalogCircuit(GatewayCircuitBreakerRegistry registry)
    {
        for (var i = 0; i < 2; i++)
        {
            var outcome = await registry.GetPipeline("catalog-service")
                .ExecuteAsync(_ => ValueTask.FromResult(Failure));

            Assert.Equal(Failure, outcome);
        }
    }

    private static GatewayCircuitBreakerRegistry CreateRegistry(
     GatewayCircuitBreakerOptions? options = null,
     TimeProvider? timeProvider = null)
     => new(
         Options.Create(options ?? new GatewayCircuitBreakerOptions
         {
             FailureRatio = 1,
             MinimumThroughput = 2,
             SamplingDurationSeconds = 30,
             BreakDurationSeconds = 60
         }),
         NullLogger<GatewayCircuitBreakerRegistry>.Instance,
         timeProvider ?? TimeProvider.System);
}
