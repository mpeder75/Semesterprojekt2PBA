# API Gateway

The gateway is an ASP.NET Core (.NET 8) application using YARP. It forwards
requests to Web, Catalog, Basket and Order, and retains local PublicApi routes
for the monolith fallback. It contains no business database.

```text
Browser -> Gateway -> Web: pages, sign-in and /User
                   -> Catalog: catalog API when enabled
                   -> Basket: basket API when enabled
                   -> Order: order API when enabled
                   -> PublicApi: legacy API/fallback, when configured
```

Production currently enables Catalog and leaves Basket and Order disabled.
PublicApi is not deployed in Azure. Web continues to handle sign-in through
ASP.NET Core Identity; a separate Identity service is not part of this change.

## Configuration and backend addresses

| File | Purpose |
| --- | --- |
| [appsettings.json](appsettings.json) | Shared routes, service metadata, flag defaults, logging and local allowed host. Contains no backend destinations. |
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
| `public-api` | `/api/{**remainder}` | 0 | `monolith-api` | `api` | 2 minutes |
| `public-api-swagger` | `/swagger/{**remainder}` in Development | 0 | `monolith-api` | None | None |
| `web` | `/{**remainder}` | 100 | `monolith-web` | None | None |

Catch-all remainders can be empty. For example, `/api/orders`, `/api/orders/`
and `/api/orders/123` all select the Order route. Gateway tests cover these
cases. Matching a route does not guarantee that the backend supports that path
or HTTP method.

All three services use the same routing implementation:

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

Use `Catalog`, `Basket` or `Order` consistently with
[ServiceRouteMetadata.cs](ServiceRouteMetadata.cs). Unknown service labels fail
configuration loading. Untagged routes keep their configured destination.

| Flag | Shared default | When false | When true | Production default |
| --- | --- | --- | --- | --- |
| `UseCatalogService` | `false` | Route to `monolith-api` | Route to `catalog-service` | `true` |
| `UseBasketService` | `false` | Return 503 for tagged Basket API routes | Route to `basket-service` | `false` |
| `UseOrderService` | `false` | Return 503 for tagged Order API routes | Route to `order-service` | `false` |

Restart the gateway after changing flags. The startup selection must initialize
before `MapReverseProxy` loads routes; [Program.cs](Program.cs) preserves and
comments this ordering. Web always requires a valid destination. Every selected
service/fallback requires at least one destination, and all its destination
addresses must be absolute HTTP or HTTPS URLs. This checks configuration, not
backend availability.

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
the Swagger restriction. Routing then selects an endpoint, followed by rate
limiting, request timeouts, the service toggle guard, timeout normalization and
YARP forwarding. `/health/gateway` is answered locally.

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
| `api` | 60 requests | 1 minute | All Catalog, Basket, Order and general API routes |
| `login` | 10 requests | 1 minute | Both login POST routes |

All routes in a policy share that IP's allowance; the API and login policies
have separate allowances. Requests above the allowance receive 429 immediately
without forwarding. There is no queue. Fixed windows permit bursts rather than
enforcing evenly spaced requests. Counters are held separately by each gateway
instance, and users sharing a public IP share an allowance on that instance.

The values are retained project defaults, not load-tested production thresholds.
Sizing remains a follow-up. Override the positive limits at startup with
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
Revisit this comparison when upgrading YARP or ASP.NET Core.

An unavailable configured backend can produce a forwarding error such as 502.
In Production, the empty legacy API cluster produces 503 for `/api/authenticate`
and unmatched `/api/...` requests. Web sign-in uses Web's separate login route.

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
does not establish that Catalog writes are protected. API authorization and
direct backend access require a separate review; no `Jwt:SigningKey` requirement
is configured by that startup code.

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
```

Enable Basket/Order by setting `FeatureManagement__UseBasketService` or
`FeatureManagement__UseOrderService` to `true` in the gateway terminal before
restarting it. Their backend database configuration must also be ready.

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
limits, production Host/HSTS policies, Development-only Swagger, and timeouts.
They do not contact Azure or require backend databases.

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

Both file checks should return `True`. Deployment verification still needs to
confirm the effective Azure environment/App Settings, backend connectivity,
client IP handling, HTTPS/HSTS, Swagger 404s, and browser sign-in/catalog requests
through the Azure gateway hostname.

## Health, observability and remaining scope

`/health/gateway` only confirms that the gateway answers requests. It does not
check downstream services or databases. Startup destination validation also
does not perform network health checks.

Startup selections and timeout warnings are logged, alongside framework/YARP
logging. Dedicated request logging, custom correlation-ID handling and
downstream readiness checks are deferred.

Circuit breaking and retries are not configured. [ADR 0001](../../docs/adr/0001-gateway-circuit-breaker.md)
proposes a later shared implementation at the gateway's outgoing HTTP boundary,
with independent state per backend. The ADR remains Proposed pending review;
implementation ownership/date still need agreement. RabbitMQ message failure
handling is a separate concern.

Rate-limit sizing, API authorization, storefront migration and deployed Azure
verification remain follow-up work as described above.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| 400 Bad Host | Effective `AllowedHosts` and the actual public request hostname. |
| Catalog fallback startup error | Catalog is OFF but `monolith-api` has no valid destination; Azure currently requires Catalog ON. |
| Basket/Order 503 Problem Details | The corresponding service flag is OFF; enable it and restart to use that API. |
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
| [ProxyTimeoutMiddleware.cs](Middleware/ProxyTimeoutMiddleware.cs) | Normalize eligible gateway deadline cancellations to 504. |
| [GatewayRateLimitingExtensions.cs](Extensions/GatewayRateLimitingExtensions.cs) | Configure the API/login policies and validate limits. |
| [GatewayForwardedHeadersExtensions.cs](Extensions/GatewayForwardedHeadersExtensions.cs) | Configure trusted forwarded IP/scheme processing. |

When adding a managed route, declare its service metadata and choose its
priority, transforms, rate policy and timeout. Extend the routing tests and
route table. A new route for an existing service does not require a route-ID
list in the filter.
