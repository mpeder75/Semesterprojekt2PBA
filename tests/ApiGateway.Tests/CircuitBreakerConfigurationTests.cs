using System.Globalization;
using System.Net;
using ApiGateway.Resilience;
using ApiGateway.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApiGateway.Tests;

public sealed class CircuitBreakerConfigurationTests
{
    [Theory]
    [InlineData("FailureRatio", "0")]
    [InlineData("FailureRatio", "-0.1")]
    [InlineData("FailureRatio", "1.1")]
    [InlineData("FailureRatio", "NaN")]
    [InlineData("MinimumThroughput", "1")]
    [InlineData("SamplingDurationSeconds", "0")]
    [InlineData("SamplingDurationSeconds", "86401")]
    [InlineData("BreakDurationSeconds", "0")]
    [InlineData("BreakDurationSeconds", "86401")]
    public void Invalid_settings_stop_startup_with_a_clear_error(string setting, string value)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Configuration = new Dictionary<string, string?>
            {
                [$"CircuitBreaker:{setting}"] = value
            }
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        var details = error.ToString();

        Assert.Contains(nameof(OptionsValidationException), details);
        Assert.Contains($"CircuitBreaker:{setting}", details);
    }

    [Theory]
    [InlineData(0.0001, 1, 1)]
    [InlineData(1.0, 86400, 86400)]
    public async Task Valid_boundary_settings_start_and_bind_correctly(
        double failureRatio, int samplingSeconds, int breakSeconds)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Configuration = new Dictionary<string, string?>
            {
                ["CircuitBreaker:FailureRatio"] = failureRatio.ToString(CultureInfo.InvariantCulture),
                ["CircuitBreaker:MinimumThroughput"] = "2",
                ["CircuitBreaker:SamplingDurationSeconds"] = samplingSeconds.ToString(CultureInfo.InvariantCulture),
                ["CircuitBreaker:BreakDurationSeconds"] = breakSeconds.ToString(CultureInfo.InvariantCulture)
            }
        });
        using var client = factory.CreateGatewayClient();
        using var response = await client.GetAsync("/health/gateway");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.Requests);

        var options = factory.Services
            .GetRequiredService<IOptions<GatewayCircuitBreakerOptions>>().Value;

        Assert.Equal(failureRatio, options.FailureRatio);
        Assert.Equal(2, options.MinimumThroughput);
        Assert.Equal(samplingSeconds, options.SamplingDurationSeconds);
        Assert.Equal(breakSeconds, options.BreakDurationSeconds);
    }
}
