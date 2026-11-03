# ADR 0001: Gateway circuit breaking per backend cluster

- Status: Proposed, pending PR review
- Date: 2026-10-27
- Updated: 2026-11-03
- Scope: Gateway-to-backend HTTP communication

## Context

The gateway forwards HTTP requests to the selected service backends. Repeated
requests to an unavailable backend can consume resources while clients wait
for errors or timeouts. Rate limits control client traffic, and request
timeouts bound individual calls; circuit breaking stops repeated forwarding
after qualifying backend failures.

This ADR records the circuit breaker implemented and verified in the gateway.
It replaces the earlier proposal to defer implementation.

## Decision and implemented scope

Use Polly.Core 8.9.0 for one shared implementation, with independent circuit
state per selected YARP backend cluster. A singleton pipeline registry retains
that state across requests and shares it across clients and protected routes.

| Protected traffic | Circuit key |
| --- | --- |
| Tagged Catalog routes, Catalog ON | `catalog-service` |
| Tagged Catalog routes, Catalog OFF | `monolith-api` |
| Tagged Basket routes, Basket ON | `basket-service` |
| Tagged Order routes, Order ON | `order-service` |

Disabled Basket/Order routes return their existing disabled-service response
before reaching the breaker. Untagged Web, login, public API catch-all and
Swagger routes bypass the breaker, even if they share a backend with a protected
route. Gateway health also bypasses it. Direct service HTTP calls, local Web
repository calls and asynchronous message handling are outside this HTTP boundary.

Circuit state is in memory in each gateway process. Separate instances have
independent state, and a process restart resets its circuits.

## Failure and recovery behavior

Count backend HTTP 500, 502, 503 and 504 when YARP reports no forwarding error.
Also count connection failures, identified downstream timeouts, normalized
gateway 504 responses paired with `RequestCanceled`, and explicit request/response
body I/O failures on the destination side. Destination body failures still count
when YARP subsequently aborts the request after a stream failure.

Ordinary 4xx responses, client cancellation, client-side I/O failures, ambiguous
proxy errors and local programming exceptions do not count as backend failures.
Rate-limit rejections and disabled-service responses occur before the breaker
and stay outside its accounting. The classifier uses both final HTTP status and
YARP forwarding errors to distinguish these outcomes.

Each permitted request is forwarded once. The response that triggers opening
is preserved. Open circuits reject protected requests without forwarding and
return HTTP 503 Problem Details with `code: circuit_open` and a `traceId`.
`Retry-After` is included when Polly supplies a retry delay. Responses that
have already started are not rewritten.

After the break, allow one recovery trial. Concurrent calls are rejected while
that trial is in progress. A successful trial closes the circuit; a qualifying
failure opens it again. Opening does not cancel requests already in flight.
Opening, half-open and closing transitions are logged with the cluster ID.

## Configuration

The checked-in defaults are:

```json
{
  "CircuitBreaker": {
    "FailureRatio": 0.5,
    "MinimumThroughput": 5,
    "SamplingDurationSeconds": 30,
    "BreakDurationSeconds": 15
  }
}
```

Opening requires the minimum number of completed outcomes within the sampling
window and a failure ratio at or above the configured threshold. Startup
validation requires a finite ratio greater than zero and at most one, minimum
throughput of at least two, and sampling/break durations from 1 to 86400 seconds.
These are project defaults, not load-tested production thresholds.

Polly supplies only circuit breaking. Existing ASP.NET Core rate limiting and
request timeouts remain in place. The gateway captures the original client
cancellation token before timeout middleware links its deadline, then classifies
the completed proxy result after timeout normalization.

## Consequences and current limits

A failing protected backend stops receiving new requests once its circuit opens.
Other cluster circuits remain independent. Automatic retries and outage fallback
are not configured; Catalog's existing startup feature-flag selection remains
separate from outage handling.

A backend 503 can represent failure of that service's own dependency. The
gateway counts that response without distinguishing the underlying cause.
Protection covers the tagged gateway HTTP routes described above.

## Verification

The recorded local Release gateway run passed all 254 tests, with zero failures
and zero skipped tests. The 87 circuit-breaker cases cover classification,
thresholds, retained state, cluster isolation, selected legacy routing,
configuration validation, rejection without forwarding, recovery and reopening,
concurrent trials, timeouts, client cancellation, local gateway rejections and
single-attempt POST forwarding.

The running local demonstration also verified two original backend 503s followed
by `circuit_open` rejection. Catalog's backend counter stayed at three while
requests were rejected, including after restoring the stub. Basket and gateway
health remained available. After the break, the trial and following request
succeeded; final counters were Catalog five and Basket two. All three circuit
transition logs were confirmed.

The [test documentation](../../tests/ApiGateway.Tests/README.md#recorded-local-verification)
records the demo settings and observations. This evidence covers local gateway
behavior with controlled backends. It does not establish deployed Azure behavior.

## References

- [Gateway documentation](../../src/ApiGateway/README.md#circuit-breaking)
- [Gateway configuration](../../src/ApiGateway/appsettings.json)
- [Gateway middleware](../../src/ApiGateway/Middleware/GatewayCircuitBreakerMiddleware.cs)
- [Failure classifier](../../src/ApiGateway/Resilience/ProxyOutcomeClassifier.cs)
- [Gateway tests](../../tests/ApiGateway.Tests/README.md)
