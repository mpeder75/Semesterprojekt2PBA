# Gateway tests

From the repository root, run:

```powershell
dotnet test tests/ApiGateway.Tests/ApiGateway.Tests.csproj --configuration Release
```

Use a .NET 8 SDK compatible with the repository's `global.json`. A successful
run reports all tests passed and returns exit code 0.

The integration tests start the actual gateway in ASP.NET Core's in-memory test
server. They load the gateway's real route configuration and use a controlled
outgoing HTTP transport. The fixture can override configuration, the clock and
the peer address, and signal when request processing finishes. Tests do not
contact Azure, local services, databases or feature-flag servers. Each test gets
its own host and configuration without process-wide environment-variable changes.

| Tests | Behavior covered |
| --- | --- |
| `ServiceRoutingTests` | All eight combinations of service flags; Catalog fallback; disabled Basket/Order returning 503; routing by metadata instead of route names; root paths, trailing slashes and nested paths; Basket path transforms; Web and login routing; methods, query strings, bodies and authorization; gateway liveness. |
| `StartupValidationTests` | Required destinations, invalid addresses, missing flags, unknown service metadata, and initialization order. Production intentionally refuses Catalog OFF while PublicApi is unavailable. |
| `TrafficControlTests` | Shared API budget, separate login budget, per-client allowances using trusted forwarded headers, forged headers, and invalid limits. |
| `ForwardedHeadersTests` | Trusted loopback and Azure private networks, IPv4 and mapped IPv6, rejection of untrusted proxy headers, and the one-hop limit. |
| `ProductionPolicyTests` | Allowed hosts, production HTTPS/HSTS behavior, backend Host headers, forwarded public host, and 503 responses for the unavailable legacy API. |
| `SwaggerPolicyTests` | Swagger UI, assets and JSON return 404 without forwarding outside Development, even with an available backend; Development keeps forwarding; alternate HTTP methods cannot bypass the guard; similar Web paths remain available. |
| `ConfigurationTests` | Local addresses belong to Development; production uses HTTPS backends and an explicit hostname; managed routes consistently declare service metadata, rate policies and timeouts. |
| `TimeoutTests` | Slow downstream calls return 504 and are canceled; client cancellation reaches the backend; backend errors and connection failures retain their status; responses already started are preserved. |

## Circuit-breaker coverage

| Tests | Cases | Behavior covered |
| --- | ---: | --- |
| [ProxyOutcomeClassifierTests](ProxyOutcomeClassifierTests.cs) | 34 | Selected backend status/transport errors, client cancellation, ambiguous/local errors, and destination stream failures combined with proxy abort. |
| [GatewayCircuitBreakerRegistryTests](GatewayCircuitBreakerRegistryTests.cs) | 12 | Pipeline reuse and concurrent creation, retained failure history, cluster/process-state isolation, local exceptions, minimum throughput, failure ratio and sampling-window expiry. |
| [CircuitBreakerHttpTests](CircuitBreakerHttpTests.cs) | 11 | Opening and rejection without forwarding, Problem Details and Retry-After, shared Catalog route state, selected legacy backend, independent services, excluded routes, and single-attempt POSTs. |
| [CircuitBreakerRecoveryTests](CircuitBreakerRecoveryTests.cs) | 4 | Successful/failed recovery trials, reopening, and one concurrent trial while other calls are rejected. |
| [CircuitBreakerConfigurationTests](CircuitBreakerConfigurationTests.cs) | 11 | Invalid settings stop startup; valid boundary values start and bind correctly. |
| [CircuitBreakerFailurePolicyTests](CircuitBreakerFailurePolicyTests.cs) | 15 | Ordinary 4xx responses, selected backend 5xx responses, connection failures, gateway deadlines, actual client cancellation, local rate-limit rejections and disabled-service responses. |

These 87 cases exercise the shared implementation with independent circuit
state per selected cluster. Each host has its own registry. FakeTimeProvider
controls circuit sampling and recovery durations; concurrent trials use
completion signals instead of timing sleeps. HTTP deadline tests use a short
test-only route timeout, and the cancellation test waits for completed gateway
accounting before checking that another request can still be forwarded.

For rejected calls, tests verify transport counts as well as the response.
This distinguishes a gateway `circuit_open` 503 from a forwarded backend 503.
Rate-limit tests also verify that local 429s do not dilute the backend failure
ratio. The [ADR](../../docs/adr/0001-gateway-circuit-breaker.md) records the
implemented decision and scope.

The timeout suite also compares the real YARP pipeline with and without
`ProxyTimeoutMiddleware`: the installed YARP version produces 400 for the
request-deadline cancellation without normalization, and 504 with it. This
documents why the middleware is necessary with the installed versions.

## Recorded local verification

The local Release gateway run on 3 November 2026 was reported successful:
**254 passed, 0 failed, 0 skipped**, including all 87 circuit-breaker cases.

A separate running-gateway demonstration used a controlled local stub to
simulate Catalog and Basket. The gateway listened on port 5180 and the stub on
5181. The stub served both logical backends in one process; Catalog failures
were controlled independently from Basket responses. This required no business
databases or message broker.

Demo overrides used failure ratio 0.5, minimum throughput 3, sampling 30 seconds,
break 15 seconds and an API allowance of 100. The checked-in minimum throughput
remains 5. The developer supplied the complete HTTP output:

| Observation | HTTP result | Catalog calls | Basket calls |
| --- | --- | ---: | ---: |
| Initial Catalog and Basket requests | Both 200 | 1 | 1 |
| First failing Catalog request | Backend 503 | 2 | 1 |
| Second failing Catalog request; circuit opens | Backend 503 | 3 | 1 |
| Open circuit, including immediately after restoring the stub | 503 with `code: circuit_open` | 3 | 1 |
| Basket during the Catalog outage | 200 | 3 | 2 |
| Gateway liveness during the outage | 200, `status: Running` | 3 | 2 |
| Recovery trial after a 17-second wait | 200 | 4 | 2 |
| Following ordinary Catalog request | 200 | 5 | 2 |

The final stub state was `catalogFailing: false`, `catalogRequests: 5` and
`basketRequests: 2`. Opening, half-open and closing transition logs were
confirmed, and both demonstration processes were stopped after the check.

## CI and validation scope

The gateway GitHub workflow runs these tests on matching pull requests and
before publishing on main. A failing test stops the build job and therefore
prevents deployment.

These tests and the local stub demonstration validate gateway behavior. They
do not establish real backend availability or business logic, deployed Azure
configuration/connectivity, or complete browser sign-in.
