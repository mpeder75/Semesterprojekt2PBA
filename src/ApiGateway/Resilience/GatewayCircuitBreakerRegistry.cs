using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;

namespace ApiGateway.Resilience;

public sealed class GatewayCircuitBreakerRegistry : IDisposable, IAsyncDisposable
{
    private static readonly ResiliencePropertyKey<bool> RecoveryTrialKey =
        new("GatewayCircuitBreaker.RecoveryTrial");

    private readonly ResiliencePipelineRegistry<string> _pipelines = new();
    private readonly GatewayCircuitBreakerOptions _options;
    private readonly ILogger<GatewayCircuitBreakerRegistry> _logger;
    private readonly TimeProvider _timeProvider;

    public GatewayCircuitBreakerRegistry(
        IOptions<GatewayCircuitBreakerOptions> options,
        ILogger<GatewayCircuitBreakerRegistry> logger,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public ResiliencePipeline<ProxyOutcome> GetPipeline(string clusterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterId);

        return _pipelines.GetOrAddPipeline<ProxyOutcome>(clusterId, builder =>
        {
            builder.TimeProvider = _timeProvider;

            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<ProxyOutcome>
            {
                FailureRatio = _options.FailureRatio,
                MinimumThroughput = _options.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(_options.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(_options.BreakDurationSeconds),

                // A cancelled trial cannot establish recovery. Keep normal client
                // cancellation excluded, but reopen after an abandoned trial.
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is null
                    && (args.Outcome.Result.IsBackendFailure
                        || (args.Outcome.Result.Reason == "client_canceled"
                            && args.Context.Properties.TryGetValue(
                                RecoveryTrialKey, out var isRecoveryTrial)
                            && isRecoveryTrial))),

                OnOpened = args =>
                {
                    _logger.LogWarning(
                        "Circuit opened for {ClusterId}. Reason: {Reason}. Break: {BreakDuration}.",
                        clusterId, args.Outcome.Result.Reason, args.BreakDuration);
                    return ValueTask.CompletedTask;
                },

                OnHalfOpened = args =>
                {
                    args.Context.Properties.Set(RecoveryTrialKey, true);
                    _logger.LogInformation(
                        "Circuit half-open for {ClusterId}; allowing a recovery trial.",
                        clusterId);
                    return ValueTask.CompletedTask;
                },

                OnClosed = _ =>
                {
                    _logger.LogInformation("Circuit closed for {ClusterId}.", clusterId);
                    return ValueTask.CompletedTask;
                }
            });
        });
    }

    public void Dispose() => _pipelines.Dispose();

    public ValueTask DisposeAsync() => _pipelines.DisposeAsync();
}
