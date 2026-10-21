# API Gateway

The API Gateway is the application's shared entry point. It receives HTTP requests and forwards them to the monolith's API, the monolith's web application, or the extracted catalog service. It is an ASP.NET Core (.NET 8) application using **YARP** (Yet Another Reverse Proxy).

Its main responsibilities are:

- Select the backend for each request using the request path and, for login, the HTTP method.
- Switch catalog traffic between the monolith and the catalog service using a feature flag.
- Limit API and login traffic per client IP address.
- Apply request timeouts to API and login routes.
- Expose a local endpoint for checking whether the gateway is running.

The gateway has no database or catalog business logic. Authentication and authorization are handled by the destination services. For example, the catalog service validates JWTs and requires the `Administrators` role for catalog item writes; the gateway forwards the request and its authorization header.

## Request flow

```text
Client -> API Gateway -> Monolith API
                      -> Monolith Web
                      -> Catalog Service (when enabled)
```

In `Program.cs`, routing selects an endpoint first. The rate limiter then checks whether the request is allowed, and the request timeout middleware applies the selected route's timeout. YARP forwards accepted proxy requests to the selected backend. An additional middleware handles certain proxy cancellation errors caused by a gateway timeout.

The gateway answers `/health/gateway` itself.

## Routing

Routes and backend addresses are configured under `ReverseProxy` in [appsettings.json](appsettings.json). A **route** describes which requests match and how to handle them. A **cluster** identifies a backend and contains its destination addresses. Each cluster currently has one destination named `local`.

Lower `Order` values have higher priority. This ensures that the specific login and catalog routes take precedence over the broader API and web routes.

| Route ID | Request match | Order | Destination cluster | Rate policy | Request timeout |
| --- | --- | ---: | --- | --- | --- |
| `api-login` | `POST /api/authenticate` | -10 | `monolith-api` | `login` | 30 seconds |
| `web-login` | `POST /Identity/Account/Login` | -10 | `monolith-web` | `login` | 30 seconds |
| `catalog-items` | `/api/catalog-items/{**remainder}` | -5 | Selected catalog backend | `api` | 30 seconds |
| `catalog-brands` | `/api/catalog-brands/{**remainder}` | -5 | Selected catalog backend | `api` | 30 seconds |
| `catalog-types` | `/api/catalog-types/{**remainder}` | -5 | Selected catalog backend | `api` | 30 seconds |
| `public-api` | `/api/{**remainder}` | 0 | `monolith-api` | `api` | 30 seconds |
| `public-api-swagger` | `/swagger/{**remainder}` | 0 | `monolith-api` | None configured | None configured |
| `web` | `/{**remainder}` | 100 | `monolith-web` | None configured | None configured |

`{**remainder}` matches the remaining path, including nested segments. Except for the two login routes, the routes do not restrict HTTP methods.

For example:

- `POST /api/authenticate` uses the login policy instead of the general API policy.
- `GET /Identity/Account/Login` uses the web catch-all; only `POST` uses the login policy.
- `GET /api/catalog-items/7` goes to the selected catalog backend.
- Other `/api/...` requests continue to go to the monolith API.
- `/swagger/index.html` goes to the monolith API, even when the catalog service is enabled.
- `/`, static assets, and other unmatched paths go to the monolith web application. A catch-all match does not guarantee the backend has a matching endpoint.

There is no configured path rewrite: the request path and query string are forwarded to the backend. Every proxy route sets `RequestHeaderOriginalHost` to `true`, preserving the incoming `Host` header instead of replacing it with the destination host.

### Backend addresses

| Cluster | Default address | Purpose |
| --- | --- | --- |
| `monolith-api` | `https://localhost:5099/` | Public API, authentication, and API Swagger |
| `monolith-web` | `https://localhost:5001/` | Web application and web login |
| `catalog-service` | `https://localhost:7101/` | Extracted catalog endpoints |

These addresses must match the actual listening addresses of the backend applications. In a container, `localhost` refers to that container, so container deployments need appropriate service addresses.

## Catalog migration feature flag

Microsoft Feature Management reads this setting:

```json
"FeatureManagement": {
  "UseCatalogService": false
}
```

- `false` (the default): catalog items, brands, and types go to `monolith-api`.
- `true`: all three catalog routes go to `catalog-service`.

This supports moving catalog functionality out of the monolith while keeping the same client-facing URLs.

[CatalogRoutingSelection.cs](CatalogRoutingSelection.cs) evaluates the flag **once during startup** and stores the selected cluster. [CatalogRoutingFilter.cs](CatalogRoutingFilter.cs) applies that selection to the three catalog route IDs, overriding their configured `ClusterId`. Restart the gateway after changing the flag; this implementation does not switch catalog backends dynamically while running.

When the flag is enabled, startup checks that the catalog cluster has at least one destination and that every configured destination address is an absolute HTTP or HTTPS URL. This validates configuration, not backend availability. The selection is logged at startup, for example:

```text
Catalog migration routing: catalog-service (UseCatalogService: True)
```

There is no automatic fallback to the monolith if the catalog service becomes unavailable. To switch back, set the flag to `false` and restart the gateway.

## Rate limiting

`Program.cs` defines two **fixed-window** rate limit policies:

| Policy | Default allowance | Window | Applied to |
| --- | ---: | --- | --- |
| `api` | 60 requests per client IP | 1 minute | General API and all catalog routes |
| `login` | 10 requests per client IP | 1 minute | Both login POST routes |

Requests sharing a policy and client IP share an allowance. For example, catalog item and catalog brand requests consume the same API allowance. API authentication and web login POSTs share the login allowance. The API and login allowances are separate from one another.

Once the allowance is exhausted, additional requests receive **HTTP 429 Too Many Requests** without reaching the backend. `QueueLimit = 0` means requests are rejected immediately instead of waiting in a queue. Permits replenish automatically for the next window. A fixed window can allow a burst near the end of one window followed by another burst in the next; it does not enforce an evenly spaced request rate.

The limits can be overridden through configuration:

```json
"Gateway": {
  "RateLimiting": {
    "ApiPermitLimit": 60,
    "LoginPermitLimit": 10
  }
}
```

These settings are optional and currently use the defaults in code. Both values must be greater than zero or startup fails. They are read at startup, so restart the gateway after changing them. The window length and queue behavior are defined in code.

Client identity comes from `HttpContext.Connection.RemoteIpAddress`, with `unknown` as the fallback. Forwarded-header processing is not configured here. If another proxy sits in front of the gateway, requests may therefore share that proxy's IP allowance. Limits are held in memory by each gateway instance; they are not shared across multiple instances.

Swagger, the web catch-all, and `/health/gateway` have no rate limit policy configured.

## Timeouts

The API, catalog, and login routes each configure:

```json
"Timeout": "00:00:30"
```

`AddRequestTimeouts()` and `UseRequestTimeouts()` enable the request timeout middleware. The configured 30 seconds is the gateway's request time budget for those routes, including waiting for the backend.

The additional middleware in `Program.cs` checks the timeout cancellation token and YARP's forwarding error. If the timeout has fired, the proxy reports `RequestCanceled` or `RequestBodyCanceled`, and the response has not started, it clears the response and returns **HTTP 504 Gateway Timeout**. It also logs the request method, path, and trace ID.

If the response has already started, the middleware cannot replace it with a 504. A gateway timeout also does not guarantee that a backend operation has stopped or rolled back; backend code must handle cancellation appropriately.

Swagger and the web catch-all have no explicit request timeout configured, and there is no global default request timeout configured. Other server, client, or proxy transport timeouts can still apply. The gateway does not configure retries or a circuit breaker.

## Run locally

Run the following commands from the repository root. The repository's `global.json` pins the .NET SDK to **8.0.425** with roll-forward disabled.

Trust the development HTTPS certificate if needed:

```powershell
dotnet dev-certs https --trust
```

Start the required backends using their own database and authentication configuration. The monolith launch profiles match the addresses listed above:

```powershell
# Run each application in its own terminal.
dotnet run --project src/PublicApi --launch-profile PublicApi
dotnet run --project src/Web --launch-profile Web
```

If catalog routing is enabled, start the catalog service as well. Its checked-in launch profile uses HTTPS port `57962`, whereas the gateway destination uses `7101`. Either update the gateway destination to the service's actual address, or explicitly run the catalog service on the gateway's expected address:

```powershell
dotnet run --project src/CatalogService.api --no-launch-profile -- --urls "https://localhost:7101"
```

The catalog service requires its own configuration, including `Jwt:SigningKey`; running the gateway does not configure or start backend services.

Start the gateway:

```powershell
dotnet run --project src/ApiGateway --launch-profile https
```

This profile listens on `https://localhost:7153` and `http://localhost:5126`. The `http` profile listens only on `http://localhost:5126`. The gateway currently allows the host name `localhost` through `AllowedHosts`; configure the actual host names when deploying elsewhere. It does not currently configure HTTP-to-HTTPS redirection.

To enable catalog routing or change limits for a local PowerShell session, set environment variables **before starting** the gateway:

```powershell
$env:FeatureManagement__UseCatalogService = "true"
$env:Gateway__RateLimiting__ApiPermitLimit = "60"
$env:Gateway__RateLimiting__LoginPermitLimit = "10"
dotnet run --project src/ApiGateway --launch-profile https
```

Double underscores represent nested configuration keys. For example, `ReverseProxy__Clusters__catalog-service__Destinations__local__Address` overrides the catalog backend address. Environment variables override the JSON settings; `appsettings.Development.json` can also supply development-specific settings.

## Checks and troubleshooting

```powershell
Invoke-RestMethod https://localhost:7153/health/gateway
Invoke-RestMethod "https://localhost:7153/api/catalog-items"
```

The health endpoint returns HTTP 200 with `{"status":"Running"}`. It confirms that the gateway can answer requests; it does not check backend services or databases.

| Symptom | What to check |
| --- | --- |
| HTTP 429 | The client IP has exhausted its allowance for the selected policy. Wait for the next window or review the configured limit. |
| HTTP 504 | A configured gateway request timeout may have expired. Check the gateway timeout log and backend logs. |
| Proxy connection or TLS error | Confirm that the backend is running, its configured address is correct, and its HTTPS certificate is trusted. |
| Catalog still reaches the monolith | Check the effective feature flag, restart the gateway, and inspect the startup routing log. |
| Startup fails with catalog destination validation | Ensure the enabled catalog cluster has destinations with valid absolute HTTP or HTTPS addresses. |
| HTTP 401 or 403 on catalog writes | Check the JWT and administrator role required by the catalog service. |

## Files to know

| File | Responsibility |
| --- | --- |
| [Program.cs](Program.cs) | Registers YARP and feature management; defines rate limits, middleware, timeout handling, and health endpoint. |
| [appsettings.json](appsettings.json) | Defines routes, backend destinations, feature flag default, logging, and allowed hosts. |
| [CatalogRoutingSelection.cs](CatalogRoutingSelection.cs) | Evaluates the feature flag at startup and validates the catalog destination configuration. |
| [CatalogRoutingFilter.cs](CatalogRoutingFilter.cs) | Applies the selected cluster to the three catalog routes. |
| [Properties/launchSettings.json](Properties/launchSettings.json) | Defines local gateway launch profiles and listening ports. |

When adding a new catalog route, update both the route configuration and the route ID selection in `CatalogRoutingFilter` if it should follow the catalog feature flag. Choose its priority, rate policy, and timeout explicitly. Keep this README in sync with those changes.
