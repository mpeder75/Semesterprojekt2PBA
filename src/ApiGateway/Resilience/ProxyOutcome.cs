namespace ApiGateway.Resilience;

public readonly record struct ProxyOutcome(
    bool IsBackendFailure,
    string Reason);
