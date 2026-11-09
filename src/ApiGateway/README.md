# API Gateway

The gateway is an ASP.NET Core (.NET 8) application using YARP. It forwards
requests to Web, Catalog, Basket, Order and Inventory, and retains local PublicApi routes
for the monolith fallback. It contains no business database.

```text
Browser -> Gateway -> Web: pages, sign-in and /User
                   -> Catalog: catalog API when enabled
                   -> Basket: basket API when enabled
                   -> Order: order API when enabled
                   -> Inventory: stock and reservation APIs when enabled
                   -> PublicApi: legacy API/fallback, when configured
```

Checked-in Production configuration enables Catalog and leaves Basket, Order and Inventory
disabled. PublicApi is not deployed in Azure. Web continues to handle sign-in through
ASP.NET Core Identity; a separate Identity service is not part of this change.

Polly.Core provides circuit breaking for the tagged Catalog, Basket, Order and Inventory
API routes, with independent state per selected backend cluster. The existing
ASP.NET Core rate limits and request timeouts remain in use.

## Configuration and backend addresses

| File | Purpose |
| --- | --- |
| [appsettings.json](appsettings.json) | Shared routes, service metadata, flag defaults, circuit-breaker settings, logging and local allowed host. Contains no backend destinations. |
| [appsettings.Development.json](appsettings.Development.json) | Local backend destinations, each named `local`. |
| [appsettings.Production.json](appsettings.Production.json) | Azure destinations named `azure`, the gateway hostname, Catalog enabled and production Host-header transforms. |

Environment-specific JSON overrides the shared file. Environment variables and
Azure App Settings can override JSON values. Use the effective configuration
when troubleshooting a deployed instance.

| Cluster | Development destination | Checked-in Production destination |
| --- | --- | --- |
| `monolith-web` | `https://localhost:5001/` | `https://app-eshop-web-esdbcdaaavgtbycw.swedencentral-01.azurewebsites.net/` |
| `catalog-service` | `https://localhost:7101/` | `https://app-eshop-catalog-cpfcatama7aqcpem.swedencentral-01.azurewebsites.net/` |
| `basket-service` | `https://localhost:7102/` | `https://app-eshop-basket-h4dudzc0cec9d6gb.swedencentral-01.azurewebsites.net/` |
| `order-service` | `https://localhost:7103/` | `https://app-eshop-order-dfd9awhue9hqacdu.swedencentral-01.azurewebsites.net/` |
| `inventory-service` | `http://localhost:57967/` | Empty destinations; disabled until a deployment address is configured. |
| `monolith-api` | `https://localhost:5099/` | Empty destinations: PublicApi is unavailable. |

`AllowedHosts` is `localhost` in shared configuration. Production allows
`eshop-yarp-gatewa-hzakhph2f9chhmc0.swedencentral-01.azurewebsites.net`.
Configure the actual public hostname when deploying another environment.

For example, these gateway App Settings override Production values:

| App Setting | Example value |
| --- | --- |
| `AllowedHosts` | The gateway's public hostname, without `https://` or a path |
| `FeatureManagement__UseCatalogService` | `true` |
| `ReverseProxy__Clusters__catalog-service__Destinations__azure__Address` | The Catalog service's HTTPS URL, ending in `/` |

Double underscores separate configuration levels. Use the destination name
`azure` for Production overrides and `local` for Development overrides.

## Routes and service flags

Lower `Order` values take priority. Login POSTs match before general API/Web
routes, and service routes match before the API catch-all.

| Route ID | Match | Order | Destination | Rate policy | Timeout |
| --- | --- | ---: | --- | --- | --- |
| `api-login` | `POST /api/authenticate` | -10 | `monolith-api` | `login` | 2 minutes |
| `web-login` | `POST /Identity/Account/Login` | -10 | `monolith-web` | `login` | 2 minutes |
| `catalog-items` | `/api/catalog-items/{**remainder}` | -5 | Selected Catalog backend | `api` | 2 minutes |
| `catalog-brands` | `/api/catalog-brands/{**remainder}` | -5 | Selected Catalog backend | `api` | 2 minutes |
| `catalog-types` | `/api/catalog-types/{**remainder}` | -5 | Selected Catalog backend | `api` | 2 minutes |
| `basket-api` | `/api/baskets/{**remainder}` | -5 | `basket-service`, or 503 when disabled | `api` | 2 minutes |
| `order-api` | `/api/orders/{**remainder}` | -5 | `order-service`, or 503 when disabled | `api` | 2 minutes |
| `inventory-api` | `/api/inventory/{**remainder}` | -5 | `inventory-service`, or 503 when disabled | `api` | 2 minutes |
| `public-api` | `/api/{**remainder}` | 0 | `monolith-api` | `api` | 2 minutes |
| `public-api-swagger` | `/swagger/{**remainder}` in Development | 0 | `monolith-api` | None | None |
| `web` | `/{**remainder}` | 100 | `monolith-web` | None | None |

Catch-all remainders can be empty. For example, `/api/orders`, `/api/orders/`
and `/api/orders/123` all select the Order route. Gateway tests cover these
cases. Matching a route does not guarantee that the backend supports that path
or HTTP method.

All four domain services use the same routing implementation:

1. [ServiceRoutingSelection.cs](ServiceRoutingSelection.cs) reads all service
   flags once at startup, chooses each destination and validates required URLs.
2. [ServiceRoutingFilter.cs](ServiceRoutingFilter.cs) selects a cluster using
   `Metadata.Service`, independently of route IDs or hardcoded path lists.
3. [ServiceToggleMiddleware.cs](Middleware/ServiceToggleMiddleware.cs) returns
   503 Problem Details for a disabled service, without forwarding the request.

Managed routes declare metadata such as:

```json
"Metadata": {
  "Service": "Catalog"
}
```

Use `Catalog`, `Basket`, `Order` or `Inventory` consistently with
[ServiceRouteMetadata.cs](ServiceRouteMetadata.cs). Unknown service labels fail
configuration loading. Untagged routes keep their configured destination.

| Flag | Shared default | When false | When true | Production default |
| --- | --- | --- | --- | --- |
| `UseCatalogService` | `false` | Route to `monolith-api` | Route to `catalog-service` | `true` |
| `UseBasketService` | `false` | Return 503 for tagged Basket API routes | Route to `basket-service` | `false` |
| `UseOrderService` | `false` | Return 503 for tagged Order API routes | Route to `order-service` | `false` |
| `UseInventoryService` | `false` | Return 503 for tagged Inventory API routes | Route to `inventory-service` | `false` |

Restart the gateway after changing flags. The startup selection must initialize
before `MapReverseProxy` loads routes; [Program.cs](Program.cs) preserves and
comments this ordering. Web always requires a valid destination. Every selected
service/fallback requires at least one destination, and all its destination
addresses must be absolute HTTP or HTTPS URLs. This checks configuration, not
backend availability.

Inventory has no monolith fallback. The root, nested stock/adjustment paths and reservation/release paths are forwarded unchanged, for all HTTP methods. In Production, Inventory has no checked-in destination; enabling its flag without configuring a valid destination fails startup with an error naming `UseInventoryService`. Its Production Host-header transform uses the backend host, as for the other service routes.

Catalog OFF in Production deliberately fails startup while `monolith-api` has
no destination. A local Catalog fallback remains supported. There is no
automatic failover when Catalog becomes unavailable.

Flags affect tagged API routes. Existing Web pages such as `/Basket` and
`/Order/MyOrders` continue through the Web catch-all.

### Path and Host-header transforms

Basket removes `/api/baskets` and adds `/basket`. For example,
`/api/baskets/customer/items` becomes `/basket/customer/items`; the root becomes
`/basket`. Other configured routes preserve their paths. Query strings and
authorization headers are forwarded.

Shared/Development routes preserve the incoming `Host` header. Production sets
`RequestHeaderOriginalHost` to `false` for the available Azure backends, so the
outgoing Host matches the backend App Service hostname. YARP also supplies the
original public host in `X-Forwarded-Host`. Production preserves Basket's path
transforms when overriding its Host transform.

## Request handling

[Program.cs](Program.cs) processes forwarded headers before production HSTS and
the Swagger restriction. Routing selects an endpoint, followed by rate limiting,
the service toggle guard, circuit breaking, request timeouts, timeout
normalization and YARP forwarding. `/health/gateway` is answered locally.

Rate-limit and disabled-service rejections happen before circuit breaking, so
they do not enter its accounting. The breaker captures the original client
cancellation token before request timeouts link in their deadline, then
classifies the completed proxy outcome after timeout normalization.

### Client IPs and rate limits

[GatewayForwardedHeadersExtensions.cs](Extensions/GatewayForwardedHeadersExtensions.cs)
processes `X-Forwarded-For` and `X-Forwarded-Proto` with a one-hop limit. Default
loopback trust is retained. When `WEBSITE_SITE_NAME` identifies Azure App
Service, the configuration additionally trusts `10.0.0.0/8`, `172.16.0.0/12` and
`192.168.0.0/16`, including their IPv4-mapped IPv6 equivalents. Untrusted peers
cannot change the selected client IP or scheme using these headers.

[GatewayRateLimitingExtensions.cs](Extensions/GatewayRateLimitingExtensions.cs)
uses the resulting `RemoteIpAddress` as the partition key, or `unknown` if absent.

| Policy | Default allowance per IP | Window | Applies to |
| --- | ---: | --- | --- |
| `api` | 60 requests | 1 minute | All Catalog, Basket, Order, Inventory and general API routes |
| `login` | 10 requests | 1 minute | Both login POST routes |

All routes in a policy share that IP's allowance; the API and login policies
have separate allowances. Requests above the allowance receive 429 immediately
without forwarding. There is no queue. Fixed windows permit bursts rather than
enforcing evenly spaced requests. Counters are held separately by each gateway
instance, and users sharing a public IP share an allowance on that instance.

The values are retained project defaults, not load-tested production thresholds.
Override the positive limits at startup with
`Gateway__RateLimiting__ApiPermitLimit` and
`Gateway__RateLimiting__LoginPermitLimit`. Zero or negative limits fail startup.
Web pages, gateway liveness and Development Swagger have no rate policy.

### Timeouts and failures

API and login routes configure `"Timeout": "00:02:00"`.
`AddRequestTimeouts()` and `UseRequestTimeouts()` apply those route budgets.
There is no global default request timeout; Web and Swagger routes have no
explicit route timeout. Other client, hosting and transport limits still apply.

[ProxyTimeoutMiddleware.cs](Middleware/ProxyTimeoutMiddleware.cs) normalizes
YARP's `RequestCanceled`/`RequestBodyCanceled` response to 504 when the gateway's
timeout token has fired and the response has not started. Ordinary downstream
errors and client cancellation are preserved. Started responses cannot be
replaced, and cancellation does not guarantee that a backend operation rolled
back.

The [timeout tests](../../tests/ApiGateway.Tests/TimeoutTests.cs) compare the real
pipeline with and without normalization: the installed YARP version returns 400
for the tested deadline cancellation without the middleware and 504 with it.

An unavailable configured backend can produce a forwarding error such as 502.
Repeated qualifying failures on a protected route can open its circuit;
subsequent calls then receive the gateway's `circuit_open` 503 response.
In Production, the empty legacy API cluster produces 503 for `/api/authenticate`
and unmatched `/api/...` requests. Web sign-in uses Web's separate login route.

### Circuit breaking

[GatewayCircuitBreakerMiddleware.cs](Middleware/GatewayCircuitBreakerMiddleware.cs)
uses Polly.Core 8.9.0 and a singleton
[pipeline registry](Resilience/GatewayCircuitBreakerRegistry.cs). Each selected
cluster has its own circuit, shared across its protected routes and clients.

| Protected routes | Circuit key |
| --- | --- |
| Tagged Catalog routes, Catalog ON | `catalog-service` |
| Tagged Catalog routes, Catalog OFF | `monolith-api` |
| Tagged Basket routes, Basket ON | `basket-service` |
| Tagged Order routes, Order ON | `order-service` |
| Tagged Inventory routes, Inventory ON | `inventory-service` |

Disabled Basket/Order/Inventory routes return their existing disabled-service response
before reaching the breaker. Untagged Web, login, public API catch-all and
Swagger routes bypass it, including when they share a cluster with a protected
route. Gateway health also bypasses it. Protection applies to HTTP requests
passing through the tagged gateway routes; direct service HTTP calls and local
Web repository calls are outside this boundary.

State is in memory in each gateway process. Gateway instances have independent
circuits, and restarting a process resets its state.

The checked-in `CircuitBreaker` settings are:

| Setting | Default | Startup validation |
| --- | --- | --- |
| `FailureRatio` | `0.5` | Finite, greater than 0 and at most 1 |
| `MinimumThroughput` | `5` | At least 2 |
| `SamplingDurationSeconds` | `30` | Between 1 and 86400 |
| `BreakDurationSeconds` | `15` | Between 1 and 86400 |

The circuit opens when the sampling window contains at least the minimum
number of completed outcomes and the backend failure ratio reaches the
configured threshold. These are explicit project defaults, not load-tested
production thresholds. Environment overrides use names such as
`CircuitBreaker__MinimumThroughput` and `CircuitBreaker__BreakDurationSeconds`.
Invalid settings stop startup.

[ProxyOutcomeClassifier.cs](Resilience/ProxyOutcomeClassifier.cs) uses both the
final HTTP status and YARP's forwarding error:

- Count backend HTTP 500, 502, 503 and 504 when no forwarding error is reported.
- Count connection failures, identified downstream timeouts and a normalized
  gateway 504 paired with YARP `RequestCanceled`, unless the client canceled.
- Count explicit destination-side request/response body I/O failures, including
  when YARP subsequently aborts the request after a stream failure.
- Ordinary 4xx responses, client cancellation, client-side I/O failures,
  ambiguous forwarding errors and local programming exceptions do not count
  as backend failures. Other status codes, including 501, are outside the
  selected server-error policy.

For a closed circuit, each permitted request is forwarded once. The failure
that opens the circuit keeps its original response. While open, protected calls
are rejected without forwarding with HTTP 503 and `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.4",
  "title": "Backend temporarily unavailable",
  "status": 503,
  "detail": "This service is temporarily unavailable. Please try again later.",
  "code": "circuit_open",
  "traceId": "example-request-trace-id"
}
```

`Retry-After` contains a positive number of seconds when Polly supplies a retry
delay. The response's `traceId` is the gateway request's trace identifier.
Responses that have already started are not rewritten.

After the break, the next request becomes the recovery trial. One trial is
allowed at a time; concurrent calls are rejected while it is in progress. A
successful trial closes the circuit, and a qualifying failure opens it again.
If the client cancels the recovery trial, the circuit opens for another full
break period because recovery was not established. Client cancellation during
normal traffic retains its exclusion from backend failures.
Opening does not cancel requests already in flight. The gateway adds no
automatic retries or outage fallback. Polly supplies only the circuit breaker;
ASP.NET Core still supplies rate limiting and request timeouts.

Backend 503 responses can represent failure of a service's own dependency. The
gateway counts those responses without distinguishing that underlying cause.

[ADR 0001](../../docs/adr/0001-gateway-circuit-breaker.md) records the implemented
decision. The [test documentation](../../tests/ApiGateway.Tests/README.md#recorded-local-verification)
records the latest 298-test gateway result and the earlier running local outage/recovery demonstration.

### HTTPS and Swagger

Azure App Service is configured with **HTTPS Only**. Verify that setting when
provisioning another instance. The gateway itself does not call
`UseHttpsRedirection`. In Production it calls `UseHsts`, with a 30-day maximum
age, no subdomain inclusion and no preload.

Outside Development, `/swagger` and its subpaths return 404 before proxying,
including UI assets and Swagger JSON. This applies regardless of backend
availability. Development forwards root Swagger requests to PublicApi, even
when Catalog is enabled. Direct backend URLs follow each backend's own policy.

## Web clients and authentication

Web handles `/Identity/Account/Login` and `/User`. The latter supplies the
browser's current user and token. Gateway Web routes forward cookies, and API
routes forward authorization headers. The gateway does not validate JWTs or
apply an API authorization policy.

The current [Catalog startup](../CatalogService.api/Program.cs) does not register
JWT bearer authentication/authorization. Forwarding a bearer token therefore
does not establish that Catalog writes are protected. No `Jwt:SigningKey`
requirement is configured by that startup code.

Web's main storefront still reads catalog data through its own repositories.
Changing `apiBase` redirects existing HTTP API clients, including BlazorAdmin;
it does not migrate the storefront's data access.

Server and browser configuration are separate:

| Environment | Web server configuration | BlazorAdmin browser configuration | Effective URLs |
| --- | --- | --- | --- |
| Development | [Web Development settings](../Web/appsettings.Development.json) | [BlazorAdmin Development settings](../BlazorAdmin/wwwroot/appsettings.Development.json) | API `https://localhost:7153/api/`, Web `https://localhost:7153/` |
| Production | [Web Production settings](../Web/appsettings.Production.json) | [BlazorAdmin Production settings](../BlazorAdmin/wwwroot/appsettings.Production.json) | API and Web URLs use the Azure gateway hostname, with `/api/` on the API base. |

Environment files override the corresponding base files. Azure App Settings can
override Web's server configuration, but do not rewrite BlazorAdmin's published
browser JSON. Keep both configurations aligned. Open the site through the
gateway hostname so browser login and API requests use the same origin.

## Run locally

Run commands from the repository root. Install a stable .NET 8 SDK compatible
with [global.json](../../global.json): baseline `8.0.100`,
`rollForward: latestMinor`, previews disabled. This permits later compatible
.NET 8 SDKs; it does not pin the machine to `8.0.425` or select .NET 9/10.
[Directory.Packages.props](../../Directory.Packages.props) controls package
versions.

Backends need their own usable database configuration. The default Windows
setup uses SQL LocalDB for Catalog and for Web's catalog/Identity databases.
The gateway itself needs no database. Trust the development certificate if
needed:

```powershell
dotnet dev-certs https --trust
```

Use three separate terminals. Explicit ports avoid relying on an untracked
local gateway or Catalog launch profile.

**Terminal 1: Catalog**

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/CatalogService.api --no-launch-profile -- --urls "https://localhost:7101"
```

**Terminal 2: Web**

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/Web --no-launch-profile -- --urls "https://localhost:5001;http://localhost:5000"
```

**Terminal 3: Gateway, with Catalog enabled**

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
$env:FeatureManagement__UseCatalogService = "true"
dotnet run --project src/ApiGateway --no-launch-profile -- --urls "https://localhost:7153;http://localhost:5126"
```

Optional local backend commands, each in its own Development terminal:

```powershell
# PublicApi is needed for Catalog OFF, legacy API calls or its Swagger UI.
dotnet run --project src/PublicApi --no-launch-profile -- --urls "https://localhost:5099"

# Start these only when testing their enabled gateway API routes.
dotnet run --project src/BasketService.api --no-launch-profile -- --urls "https://localhost:7102"
dotnet run --project src/OrderService.api --no-launch-profile -- --urls "https://localhost:7103"
dotnet run --project src/InventoryService.api --no-launch-profile -- --urls "http://localhost:57967"
```

Enable Basket/Order by setting `FeatureManagement__UseBasketService` or
`FeatureManagement__UseOrderService` to `true` in the gateway terminal before
restarting it. Their backend database configuration must also be ready.

For Inventory, set `$env:FeatureManagement__UseInventoryService = "true"` in the gateway terminal before starting/restarting the gateway. Start Inventory on the HTTP port above, then use URLs such as `https://localhost:7153/api/inventory/1` and `https://localhost:7153/api/inventory/reservations`. Inventory's own Swagger remains at `http://localhost:57967/swagger`; gateway root Swagger keeps its existing PublicApi behavior. Inventory API requests need only Inventory and the gateway running. See [Inventory's README](../InventoryService.api/README.md) for create/reserve/release examples.

In another terminal, check:

```powershell
curl.exe -i https://localhost:7153/health/gateway
curl.exe -i "https://localhost:7153/api/catalog-items?pageSize=1&pageIndex=0"
curl.exe -i https://localhost:7153/api/catalog-brands
curl.exe -i https://localhost:7153/api/catalog-types
```

Expect 200 and `{"status":"Running"}` from liveness, and 200 with data from the
three Catalog routes. Open
[admin sign-in through the gateway](https://localhost:7153/Identity/Account/Login?returnUrl=%2Fadmin),
sign in with an administrator account, and check browser Network requests.
Catalog calls should use port `7153` and return 200. Do not use a storefront
page loading as proof that it uses the Catalog API.

## Docker

The [Dockerfile](Dockerfile) builds a Linux image with the .NET 8 SDK and runs
the published gateway using the ASP.NET Core runtime, non-root user `app` and
HTTP port `8080`. Use Docker Desktop's Linux container engine.

From the repository root:

```powershell
docker build -f src/ApiGateway/Dockerfile -t eshop-gateway:local .
docker run --rm -p 127.0.0.1:7155:8080 -e ASPNETCORE_ENVIRONMENT=Production -e AllowedHosts=localhost eshop-gateway:local
```

In another terminal:

```powershell
curl.exe -i http://localhost:7155/health/gateway
```

Expect 200 and `{"status":"Running"}`. This uses Production configuration,
including the Azure backends and enabled Catalog flag; the host override permits
the local check. Override destinations for other environments. Container
`localhost` refers to the container itself.

The image build and liveness check have been verified locally. The gateway CI
workflow currently builds the .NET application, not the Docker image. Azure's
existing Windows App Service deployment uses the published application rather
than this Linux image.

## Tests, publishing and CI

Run the [gateway test suite](../../tests/ApiGateway.Tests/README.md):

```powershell
dotnet test tests/ApiGateway.Tests/ApiGateway.Tests.csproj --configuration Release
```

Tests use the real gateway pipeline and route configuration with controlled
outgoing HTTP responses. They cover all flag combinations, metadata routing,
root paths, Basket transforms, startup validation, forwarded headers, rate
limits, production Host/HSTS policies, Development-only Swagger, timeouts and
circuit breaking. Breaker coverage includes thresholds, per-cluster isolation,
short-circuit rejection, recovery and reopening, concurrent recovery trials,
failure classification, configuration validation and single-attempt writes.
The 4 November 2026 gateway run passed all 298 cases, including Inventory route/toggle, timeout/rate-limit, outage isolation and recovery checks. Gateway tests do not contact Azure or require backend databases. The separate shared integration project also tests YARP forwarding into the actual Inventory application with SQL Server; that suite requires the documented test database prerequisites.

[Gateway CI](../../.github/workflows/gateway.yml) runs on matching pull requests
to main, matching pushes to main and manual dispatches. Its Windows build job
installs a .NET 8 SDK, restores, builds, tests and publishes the gateway. Deploy
requires that job to succeed and is restricted to main pushes/manual dispatches.
It targets App Service `eshop-yarp-gateway` using repository secret
`YARP_PUBLISH_PROFILE`. Pull requests do not deploy. These are workflow rules,
not a claim that the changed workflow has already run successfully in GitHub.

[Web CI](../../.github/workflows/dotnet.yml) separately publishes and deploys Web.
Both Web and gateway need their changes deployed for production client routing.
To inspect packages locally:

```powershell
dotnet publish src/ApiGateway/ApiGateway.csproj --configuration Release --output ./artifacts/gateway
dotnet publish src/Web/Web.csproj --configuration Release --output ./artifacts/web
Test-Path ./artifacts/web/appsettings.Production.json
Test-Path ./artifacts/web/wwwroot/appsettings.Production.json
```

Both file checks should return `True`. The circuit-breaker verification recorded
here covers the automated gateway suite and a local controlled-backend demo;
it does not establish the effective Azure settings or deployed browser flows.

## Health and observability

`/health/gateway` only confirms that the gateway answers requests. It does not
check downstream services or databases. Startup destination validation also
does not perform network health checks.

Startup selections, timeout warnings and circuit transitions are logged,
alongside framework/YARP messages. Opening is logged at Warning level; half-open
and closed transitions are logged at Information level. The transition messages
include the cluster ID:

```text
Circuit opened for catalog-service. Reason: backend_server_error. Break: 00:00:15.
Circuit half-open for catalog-service; allowing a recovery trial.
Circuit closed for catalog-service.
```

Liveness remains available while a backend circuit is open. Its response does
not expose circuit state or downstream readiness.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| 400 Bad Host | Effective `AllowedHosts` and the actual public request hostname. |
| Catalog fallback startup error | Catalog is OFF but `monolith-api` has no valid destination; Azure currently requires Catalog ON. |
| 503 with `Basket API is disabled`, `Order API is disabled` or `Inventory API is disabled` | The corresponding service flag is OFF; enable it and restart to use that API. |
| 503 with `code: circuit_open` | The selected backend circuit is open or a recovery trial is in progress. Inspect its transition logs and the backend; use `Retry-After` when present. |
| Circuit-breaker options validation error | Check the effective `CircuitBreaker` settings against the ranges above. |
| Legacy API 503 in Production | PublicApi has no Azure destination; Web sign-in uses the Web login route. |
| Swagger 404 outside Development | Expected gateway policy, including JSON and UI assets. |
| 429 | That IP exhausted the policy budget on this instance; API and login have separate budgets. |
| 504 | A gateway route deadline may have expired; inspect timeout logs and backend behavior. |
| 502/connection failure | Check backend availability, configured address and trusted HTTPS certificate. |
| Browser still calls an old port | Inspect the served `appsettings.Development.json`/Production file as well as the base file, then reload the browser. |
| Browser preflight errors | Confirm the page and API use the gateway origin, then inspect the actual target URL and connectivity. |
| Web login throws a SQL/LocalDB error | Check Web's Identity connection and database startup/seeding errors; gateway routing does not supply the Identity database. |

## Source map

| File | Responsibility |
| --- | --- |
| [Program.cs](Program.cs) | Registrations, initialization order, middleware pipeline, Swagger guard, liveness and proxy mapping. |
| [ServiceRoutingSelection.cs](ServiceRoutingSelection.cs) | Shared startup flag selection and required destination validation. |
| [ServiceRouteMetadata.cs](ServiceRouteMetadata.cs) | Service metadata key and supported labels. |
| [ServiceRoutingFilter.cs](ServiceRoutingFilter.cs) | Apply cluster selection by service metadata. |
| [ServiceToggleMiddleware.cs](Middleware/ServiceToggleMiddleware.cs) | Return 503 for disabled managed API routes. |
| [GatewayCircuitBreakerMiddleware.cs](Middleware/GatewayCircuitBreakerMiddleware.cs) | Wrap protected forwarding, classify outcomes and return `circuit_open` responses. |
| [GatewayCircuitBreakerRegistry.cs](Resilience/GatewayCircuitBreakerRegistry.cs) | Retain one Polly pipeline per cluster, configure thresholds and log transitions. |
| [GatewayCircuitBreakerOptions.cs](Resilience/GatewayCircuitBreakerOptions.cs) | Define the circuit-breaker configuration section and defaults. |
| [ProxyOutcome.cs](Resilience/ProxyOutcome.cs) | Represent the proxy result used by the breaker. |
| [ProxyOutcomeClassifier.cs](Resilience/ProxyOutcomeClassifier.cs) | Distinguish backend failures from client/local proxy outcomes. |
| [ProxyTimeoutMiddleware.cs](Middleware/ProxyTimeoutMiddleware.cs) | Normalize eligible gateway deadline cancellations to 504. |
| [GatewayRateLimitingExtensions.cs](Extensions/GatewayRateLimitingExtensions.cs) | Configure the API/login policies and validate limits. |
| [GatewayForwardedHeadersExtensions.cs](Extensions/GatewayForwardedHeadersExtensions.cs) | Configure trusted forwarded IP/scheme processing. |

Managed routes declare service metadata, priority, transforms, rate policy and
timeout. Routing and circuit protection use that metadata and the selected
cluster, rather than route-ID lists.
