# ADR 0001: Defer gateway circuit breaking

- Status: Proposed, pending PR review
- Date: 2026-10-27
- Scope: Gateway-to-backend HTTP communication

## Context

The YARP gateway forwards HTTP requests to Web and the selected domain-service
backends. Repeated calls to an unavailable backend can consume resources while
clients wait for errors or timeouts.

The gateway currently has startup validation for required destinations,
per-client API and login rate limits, and two-minute request timeouts on API and
login routes. `/health/gateway` checks gateway liveness only. These controls do
not provide a circuit breaker: permitted requests can still repeatedly attempt
to reach an unavailable backend. Web catch-all requests have no explicit route
timeout.

The project requirements list circuit breaking as a possible error-handling
mechanism. The PR review permits either an implementation or an ADR explaining
its placement. This document proposes a deferral; review acceptance is still
required.

## Decision

Defer circuit-breaker implementation from this gateway PR. Complete deployment
and end-to-end routing verification first, then address circuit breaking in a
separate resilience change.

The planned location is the gateway's outgoing HTTP boundary. Use one shared
implementation with independent state per YARP backend cluster. Catalog,
Basket and Order must follow the same pattern, so a failing Catalog backend
does not open the circuit for Basket or Order. Apply the same approach to other
proxied backends when included in the implementation scope.

Plan to count connection failures, downstream timeouts and selected server
errors. Ordinary client errors, such as 401, 403 and 404, and client-initiated
cancellation should not trip a circuit. Configure thresholds and break duration
explicitly during implementation.

While a circuit is open, return 503 without forwarding. After the break duration,
allow limited trial traffic, resuming normal traffic on recovery or reopening
the circuit on continued failure. Keep automatic retries disabled for forwarded
requests and do not add automatic fallback to PublicApi.

RabbitMQ failure handling will be designed separately for asynchronous messages.
Queueing, acknowledgements and redelivery do not protect HTTP requests that
bypass the broker.

## Rationale and consequences

This keeps the current PR focused on routing, environment configuration and
deployment validation. A separate change allows breaker state, failure
classification and recovery behavior to be designed and tested together.

Until that change is implemented, repeated backend failures are still attempted
within the existing request limits. The existing timeouts and rate limits are
not equivalent to circuit breaking. This ADR records the remaining gap; it does
not claim that the resilience feature is complete.

## Follow-up and acceptance

The proposed implementation milestone is the resilience change following Azure
end-to-end routing verification. An individual owner and target date are not yet
assigned; agree these during PR review and record the implementation issue here.

Acceptance tests must cover opening, immediate rejection while open, limited
recovery trials, recovery and reopening, isolation between clusters, exclusion
of client errors and cancellation, and unchanged feature-flag behavior. Verify
that requests are not automatically retried. The existing gateway tests cover
timeouts and routing, not a circuit breaker.

Keep this ADR **Proposed** until the reviewer accepts the deferral and follow-up.

## References

- [Gateway configuration](../../src/ApiGateway/appsettings.json)
- [Current gateway tests](../../tests/ApiGateway.Tests/README.md)
- [Microsoft: Circuit Breaker pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- [RabbitMQ: Reliability guide](https://www.rabbitmq.com/docs/reliability)
