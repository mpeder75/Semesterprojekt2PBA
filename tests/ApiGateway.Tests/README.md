# Gateway tests

From the repository root, run:

```powershell
dotnet test tests/ApiGateway.Tests/ApiGateway.Tests.csproj --configuration Release
```

Use a .NET 8 SDK compatible with the repository's `global.json`. A successful
run reports all tests passed and returns exit code 0.

The integration tests start the actual gateway in ASP.NET Core's in-memory test
server. They load the gateway's real route configuration and replace only the
outgoing HTTP transport with controlled responses. They do not contact Azure,
local services, databases, or feature-flag servers. Each test gets its own host
and configuration; tests do not change process-wide environment variables.

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

The timeout suite also compares the real YARP pipeline with and without
`ProxyTimeoutMiddleware`: the installed YARP version produces 400 for the
request-deadline cancellation without normalization, and 504 with it. This
documents why the middleware is currently necessary. Revisit that comparison
when upgrading YARP or ASP.NET Core.

The gateway GitHub workflow runs these tests on matching pull requests and
before publishing on main. A failing test stops the build job and therefore
prevents deployment.

These tests validate gateway behavior, not the availability or business logic of
the real backends. Azure proxy headers, deployed configuration, connectivity,
and complete browser sign-in still need verification after deployment.
