using System.Net;
using System.Text.Json;
using ApiGateway.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace ApiGateway.Tests;

public sealed class CircuitBreakerRecoveryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recovery_trial_closes_or_reopens_the_circuit(bool trialSucceeds)
    {
        var clock = new FakeTimeProvider();
        var backendHealthy = false;

        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            TimeProvider = clock,
            Configuration = RecoverySettings(),
            Backend = (_, _) => Task.FromResult(new HttpResponseMessage(
                backendHealthy
                    ? HttpStatusCode.OK
                    : HttpStatusCode.InternalServerError))
        });
        using var client = factory.CreateGatewayClient();

        await AssertResponse(client, factory, HttpStatusCode.InternalServerError, 1);
        await AssertResponse(client, factory, HttpStatusCode.InternalServerError, 2);

        backendHealthy = trialSucceeds;
        clock.Advance(TimeSpan.FromSeconds(59));
        await AssertResponse(client, factory, HttpStatusCode.ServiceUnavailable, 2);

        clock.Advance(TimeSpan.FromSeconds(2));
        await AssertResponse(
            client,
            factory,
            trialSucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError,
            3);

        if (trialSucceeds)
        {
            await AssertResponse(client, factory, HttpStatusCode.OK, 4);
            return;
        }

        await AssertResponse(client, factory, HttpStatusCode.ServiceUnavailable, 3);

        backendHealthy = true;
        clock.Advance(TimeSpan.FromSeconds(59));
        await AssertResponse(client, factory, HttpStatusCode.ServiceUnavailable, 3);

        clock.Advance(TimeSpan.FromSeconds(2));
        await AssertResponse(client, factory, HttpStatusCode.OK, 4);
        await AssertResponse(client, factory, HttpStatusCode.OK, 5);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_one_recovery_trial_is_forwarded_while_half_open(bool trialSucceeds)
    {
        var clock = new FakeTimeProvider();
        var trialEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTrial = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var backendCalls = 0;

        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            TimeProvider = clock,
            Configuration = RecoverySettings(),
            Backend = async (_, token) =>
            {
                var call = Interlocked.Increment(ref backendCalls);

                if (call <= 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                if (call == 3)
                {
                    trialEntered.TrySetResult();
                    await releaseTrial.Task.WaitAsync(token);
                    return new HttpResponseMessage(trialSucceeds
                        ? HttpStatusCode.OK
                        : HttpStatusCode.InternalServerError);
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        });
        using var client = factory.CreateGatewayClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        await AssertResponse(client, factory, HttpStatusCode.InternalServerError, 1);
        await AssertResponse(client, factory, HttpStatusCode.InternalServerError, 2);
        clock.Advance(TimeSpan.FromSeconds(61));

        var trial = client.GetAsync("/api/catalog-items");

        try
        {
            await trialEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var competing = Enumerable.Range(0, 6)
                .Select(_ => client.GetAsync("/api/catalog-items"))
                .ToArray();
            var responses = await Task.WhenAll(competing)
                .WaitAsync(TimeSpan.FromSeconds(5));

            try
            {
                foreach (var response in responses)
                {
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                    using var body = JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync());
                    Assert.Equal("circuit_open", body.RootElement.GetProperty("code").GetString());
                }

                Assert.False(trial.IsCompleted);
                Assert.Equal(3, factory.Requests.Count);
                Assert.Equal(3, Volatile.Read(ref backendCalls));
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            releaseTrial.TrySetResult();
        }

        using var trialResponse = await trial.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            trialSucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError,
            trialResponse.StatusCode);
        Assert.Equal(3, factory.Requests.Count);

        await AssertResponse(
            client,
            factory,
            trialSucceeds ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
            trialSucceeds ? 4 : 3);
    }

    private static async Task AssertResponse(
        HttpClient client,
        GatewayFactory factory,
        HttpStatusCode expectedStatus,
        int expectedBackendCalls)
    {
        using var response = await client.GetAsync("/api/catalog-items");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedBackendCalls, factory.Requests.Count);

        if (expectedStatus == HttpStatusCode.ServiceUnavailable)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("circuit_open", body.RootElement.GetProperty("code").GetString());
        }
    }


    private static Dictionary<string, string?> RecoverySettings()
        => new()
        {
            ["CircuitBreaker:FailureRatio"] = "1",
            ["CircuitBreaker:MinimumThroughput"] = "2",
            ["CircuitBreaker:SamplingDurationSeconds"] = "30",
            ["CircuitBreaker:BreakDurationSeconds"] = "60"
        };
}
