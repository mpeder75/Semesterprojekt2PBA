# Catalog Migration Toggle — Step 2.1.7

**Prepared:** 20 October 2026.  
**Status:** Proposed code passed isolated .NET 8 / YARP 2.3 checks. Developer application edits and runtime checks are pending. The assistant has prepared this guide and a separate probe; it has not applied the application changes below.  
**Related guide:** [Gateway routing and migration controls](gateway-routing.md).

The gateway will select a destination for Catalog API requests using `Gateway:Migration:UseCatalogService`. A missing setting means false. Read the setting at startup; stop and restart the gateway when changing it.

| Setting | Catalog API destination |
| --- | --- |
| `false` | Existing PublicApi, cluster `monolith-api` |
| `true` | Configured Catalog destination, cluster `catalog-service` |

Prepare and test this routing while the monolith remains the active business implementation. After the team handoff below, prefer the reviewed real Catalog service for integration checks; a temporary backend without database access remains available for routing-only checks. Complete the remaining Catalog extraction requirements, internal caller changes, data transfer, and writer activation in their coordinated phases. A gateway switch cannot redirect Web's internal repository calls.

## Team handoff — Catalog work is already on main

**Reviewed remotely:** `main` at commit `d0810a5cb2c3efeea68ba1c556cef8c71c437322` on 20 October 2026. That revision is 13 commits ahead of the current local branch's committed revision, `09ba6a45df779ed4c15ad4d3ba22dd978a44aa21`. The local branch is `xzar/NewService`, with uncommitted gateway/configuration changes. This inspection did not merge branches or run the remote Catalog code.

The newer [Catalog startup](https://github.com/mpeder75/Semesterprojekt2PBA/blob/d0810a5cb2c3efeea68ba1c556cef8c71c437322/src/CatalogService.api/Program.cs) registers its database, repositories, mapping, Swagger, and minimal endpoints. Its product API paths match the planned gateway routes. Reuse the teammate's work and review the remaining integration requirements before replacing business traffic.

Recommended sequence now:

1. Review and commit the current gateway work on `xzar/NewService`, including new gateway files and the relevant configuration/docs. Fetch the remote and merge `origin/main` into that working branch. Keep the gateway work and teammate changes together; review any conflicts and ensure central package versions retain both YARP and the Catalog additions.
2. Build and start the merged Catalog service separately using a dedicated disposable Catalog database for integration checks. Its seed code runs migrations, so confirm the connection target before starting it. Keep the accepted monolith databases as the baseline.
3. Check product/brand/type reads, item lookup, pagination/filter contracts, image URLs, and administrator authorization. Startup success alone is not completion of Catalog extraction. The inspected create/update/delete endpoint declarations have their authorization attributes commented out, and startup does not register JWT bearer authentication. Complete and verify that access path before enabling the full Catalog route switch. The gateway forwards credentials; Catalog must enforce its write permissions.
4. Implement Part A with the switch OFF. Once the real service passes these checks, use its confirmed local address as the `catalog-service` destination and test ON against that service. The temporary backend in steps 6–7 becomes an optional fallback for testing routing while Catalog gaps are being completed.
5. For a real-service test, replace the probe-marker assertions with evidence from Catalog request logs and its disposable test data. Verify anonymous writes are rejected and administrator writes work using that isolated database. Keep ordinary business traffic on the monolith afterward; coordinate the eventual data migration, internal caller changes, product-event integration, and single active writer before real activation.

Source examples for the authorization gap: [create endpoint](https://github.com/mpeder75/Semesterprojekt2PBA/blob/d0810a5cb2c3efeea68ba1c556cef8c71c437322/src/CatalogService.api/EndPoints/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs), [update endpoint](https://github.com/mpeder75/Semesterprojekt2PBA/blob/d0810a5cb2c3efeea68ba1c556cef8c71c437322/src/CatalogService.api/EndPoints/CatalogItemEndpoints/UpdateCatalogItemEndpoint.cs), and [delete endpoint](https://github.com/mpeder75/Semesterprojekt2PBA/blob/d0810a5cb2c3efeea68ba1c556cef8c71c437322/src/CatalogService.api/EndPoints/CatalogItemEndpoints/DeleteCatalogItemEndpoint.cs). These are source findings from the reviewed revision, not runtime test results.

The gateway and native RabbitMQ foundation can continue while the Catalog owner completes these integration requirements. No Catalog work needs to be recreated solely to follow the original sequence.

## Part A — Implement the switch and verify the monolith path

### 1. Add the default setting

Stop the gateway with Ctrl+C. Keep PublicApi and Web running. The verified timeout setup should be restored: `AddRequestTimeouts()` plus the cancellation-aware response handler, with normal route deadlines of 30 seconds. Remove the temporary `/timeout-test` endpoint if it still exists.

In `src/ApiGateway/appsettings.json`, insert the following root-level entry after `AllowedHosts` and before `Logging`. Keep the existing logging, routes, and clusters.

```json
"Gateway": {
  "Migration": {
    "UseCatalogService": false
  }
},
```

**Purpose:** Give the gateway a named migration setting that starts with the existing implementation selected. The setting gains an effect when the filter in step 3 is registered.

### 2. Add three Catalog routes

Inside `ReverseProxy` → `Routes`, insert these entries before `public-api`:

```json
"catalog-items": {
  "ClusterId": "monolith-api",
  "Order": -5,
  "Timeout": "00:00:30",
  "RateLimiterPolicy": "api",
  "Match": { "Path": "/api/catalog-items/{**remainder}" },
  "Transforms": [ { "RequestHeaderOriginalHost": "true" } ]
},
"catalog-brands": {
  "ClusterId": "monolith-api",
  "Order": -5,
  "Timeout": "00:00:30",
  "RateLimiterPolicy": "api",
  "Match": { "Path": "/api/catalog-brands/{**remainder}" },
  "Transforms": [ { "RequestHeaderOriginalHost": "true" } ]
},
"catalog-types": {
  "ClusterId": "monolith-api",
  "Order": -5,
  "Timeout": "00:00:30",
  "RateLimiterPolicy": "api",
  "Match": { "Path": "/api/catalog-types/{**remainder}" },
  "Transforms": [ { "RequestHeaderOriginalHost": "true" } ]
},
```

**Purpose:** Identify the Catalog paths separately from the broad `/api/{**remainder}` route. Order -5 takes precedence over that route's order 0. The catch-all suffix matches both the collection path and item paths. All HTTP methods retain their path and query string. The existing shared `api` rate-limit policy, 30-second deadline, and original-host transform remain on these routes. Authentication stays in the receiving API.

The existing `api-login`, `web-login`, `public-api`, Swagger, and Web routes stay in place. Initially these new routes also name `monolith-api`; the configuration filter chooses the effective Catalog destination.

**Timeout-test adjustment:** Once these routes exist, a timeout test of `/api/catalog-items` must change the `catalog-items` route's deadline. Changing the broader `public-api` deadline no longer affects that request.

### 3. Add and register the route-selection code

Create `src/ApiGateway/CatalogRoutingFilter.cs` with this content:

```csharp
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public sealed class CatalogRoutingFilter : IProxyConfigFilter
{
    private readonly string _catalogClusterId;

    public CatalogRoutingFilter(
        IConfiguration configuration,
        ILogger<CatalogRoutingFilter> logger)
    {
        var useCatalogService = configuration.GetValue<bool>(
            "Gateway:Migration:UseCatalogService");

        _catalogClusterId = useCatalogService
            ? "catalog-service"
            : "monolith-api";

        if (useCatalogService)
        {
            var destinations = configuration.GetSection(
                "ReverseProxy:Clusters:catalog-service:Destinations")
                .GetChildren().ToArray();

            if (destinations.Length == 0 || destinations.Any(destination =>
                !Uri.TryCreate(destination["Address"], UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttp
                    && address.Scheme != Uri.UriSchemeHttps)))
            {
                throw new InvalidOperationException(
                    "UseCatalogService is enabled, but catalog-service requires " +
                    "at least one destination with a valid HTTP or HTTPS address.");
            }
        }

        logger.LogInformation(
            "Catalog migration routing: {ClusterId} (UseCatalogService: {Enabled})",
            _catalogClusterId,
            useCatalogService);
    }

    public ValueTask<ClusterConfig> ConfigureClusterAsync(
        ClusterConfig cluster, CancellationToken cancel)
        => ValueTask.FromResult(cluster);

    public ValueTask<RouteConfig> ConfigureRouteAsync(
        RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        var isCatalogRoute = route.RouteId is
            "catalog-items" or "catalog-brands" or "catalog-types";

        return ValueTask.FromResult(isCatalogRoute
            ? route with { ClusterId = _catalogClusterId }
            : route);
    }
}
```

**Purpose:** Select a cluster once at startup, reject enabled routing without a usable HTTP/HTTPS destination configuration, and change only the three named Catalog routes. A valid address is a configuration check; it does not prove that a service is running or ready.

In `src/ApiGateway/Program.cs`, add this using alongside the existing imports:

```csharp
using ApiGateway;
```

Replace the existing reverse-proxy registration with:

```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddConfigFilter<CatalogRoutingFilter>();
```

Keep the timeout registrations/handler, rate-limit policies, middleware order, health endpoint, and `app.MapReverseProxy()`.

YARP supports [configuration filters](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-filters?view=aspnetcore-10.0) that modify configuration before it is applied. This filter's selected cluster is stored in its constructor, so changing the toggle requires a gateway restart even though YARP supports some configuration reloads. No new NuGet package is needed.

### 4. Build, start with the switch OFF, and test

In the gateway PowerShell window, from the repository folder:

```powershell
$env:Gateway__Migration__UseCatalogService = 'false'
dotnet build src/ApiGateway/ApiGateway.csproj --no-restore
```

Once the build succeeds:

```powershell
dotnet run --project src/ApiGateway/ApiGateway.csproj --no-launch-profile --urls 'https://localhost:7001' --no-build --no-restore
```

Expect the startup log to include:

```text
Catalog migration routing: monolith-api (UseCatalogService: False)
```

In the testing PowerShell window:

```powershell
curl.exe -sS -o NUL -w "Products: %{http_code}\n" 'https://localhost:7001/api/catalog-items?pageSize=1&pageIndex=0'
curl.exe -sS -o NUL -w "Brands: %{http_code}\n" 'https://localhost:7001/api/catalog-brands'
curl.exe -sS -o NUL -w "Types: %{http_code}\n" 'https://localhost:7001/api/catalog-types'
curl.exe -sS -o NUL -w "Gateway health: %{http_code}\n" 'https://localhost:7001/health/gateway'
curl.exe -sS -o NUL -w "Web login page: %{http_code}\n" 'https://localhost:7001/Identity/Account/Login'
```

Expect all five to return 200. Confirm ordinary browsing and login still work. Keep the gateway log and statuses. Part A completes implementation of the selector and verification of the monolith path; the positive enabled-path test in Part B is still required.

## Part B — Verify the guard and the enabled path

After the team handoff above, prefer the reviewed real Catalog service for the positive enabled-path test once it passes readiness checks. Steps 6–7 retain the temporary-backend option. In either case, test the missing-configuration guard and restore the normal switch to OFF afterward. Remove a real-service destination after the exercise if it is not yet ready for normal activation; the temporary probe destination must be removed as described in step 8.

### 5. Check an enabled switch with no destination

At this point there should be no `catalog-service` cluster in `appsettings.json`. Stop the gateway. In that same gateway window:

```powershell
$env:Gateway__Migration__UseCatalogService = 'true'
dotnet run --project src/ApiGateway/ApiGateway.csproj --no-launch-profile --urls 'https://localhost:7001' --no-build --no-restore
```

Expected: the gateway fails to start with an error containing:

```text
UseCatalogService is enabled, but catalog-service requires at least one destination with a valid HTTP or HTTPS address.
```

This is an intentional configuration test: traffic must not silently fall back to the monolith when an enabled destination is missing. Save the message, then return the environment override to false. PublicApi and Web remain running.

### 6. Create a temporary routing-test backend

In another PowerShell window, from the repository folder:

```powershell
dotnet new web --name GatewayRoutingProbe --output tools/GatewayRoutingProbe --framework net8.0 --no-restore
```

Replace `tools/GatewayRoutingProbe/Program.cs` with:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/api/{**remainder}", (HttpContext context) =>
{
    context.Response.Headers["X-Gateway-Routing-Probe"] = "catalog-test-backend";

    return Results.Ok(new
    {
        source = "catalog-test-backend",
        path = context.Request.Path.Value,
        query = context.Request.QueryString.Value
    });
});

app.Run();
```

Start it:

```powershell
dotnet run --project tools/GatewayRoutingProbe/GatewayRoutingProbe.csproj --no-launch-profile --urls 'http://localhost:7205'
```

**Purpose:** Return an unmistakable marker so the selected destination can be observed. This tool has no business database, users, or write endpoints. It is development test infrastructure, not a sixth business microservice or the real Catalog implementation. Its JSON is diagnostic output, so use the explicit test requests while enabled rather than exercising the business UI against it.

### 7. Configure the temporary destination and verify ON

In the gateway's `appsettings.json`, add this entry inside `ReverseProxy` → `Clusters`, keeping the two existing monolith clusters. Add the necessary comma between entries.

```json
"catalog-service": {
  "Destinations": {
    "local": {
      "Address": "http://localhost:7205/"
    }
  }
}
```

In the gateway PowerShell window:

```powershell
$env:Gateway__Migration__UseCatalogService = 'true'
dotnet build src/ApiGateway/ApiGateway.csproj --no-restore
```

Once the build succeeds, start with the same gateway run command from step 4. Expect:

```text
Catalog migration routing: catalog-service (UseCatalogService: True)
```

In the testing PowerShell window:

```powershell
curl.exe -sS -D - 'https://localhost:7001/api/catalog-items?pageSize=1&pageIndex=0'
curl.exe -sS -D - 'https://localhost:7001/api/catalog-items/1'
curl.exe -sS -D - 'https://localhost:7001/api/catalog-brands'
curl.exe -sS -D - 'https://localhost:7001/api/catalog-types'
```

Expect 200 and `X-Gateway-Routing-Probe: catalog-test-backend` on all four responses. The JSON should show the requested path and preserve the query string. These are test-backend responses, not real catalog records.

Repeat the health and Web login-page requests from step 4; expect 200. Also check `/swagger/index.html` remains available through the gateway and has no routing-probe marker. These unrelated routes should still use their existing destinations.

If an enabled, syntactically valid destination is stopped, forwarding returns a backend connection failure; it is not a reason to switch business writes automatically to another database. Complete readiness and cutover checks before enabling a real Catalog service.

### 8. Restore the development setup to OFF

Stop the gateway. Remove the temporary `catalog-service` cluster entry from its JSON, retaining the two monolith clusters. Keep the JSON toggle false. In the gateway PowerShell window:

```powershell
$env:Gateway__Migration__UseCatalogService = 'false'
dotnet build src/ApiGateway/ApiGateway.csproj --no-restore
```

Start the gateway with the step 4 run command, and stop the routing-probe tool with Ctrl+C. Keep its source as a reusable development tool. Repeat the five checks from step 4 and confirm ordinary browsing and customer/admin login. Catalog responses should again contain real monolith data and have no probe marker.

Environment variables override JSON. The final override must therefore be false in the window that launches the gateway. New PowerShell windows do not inherit overrides set only in another already-open window.

## Evidence and completion

An isolated .NET 8 / YARP 2.3 in-memory probe using the filter above passed:

- Missing flag and explicit false: seven Catalog read/write route checks reached the controlled monolith backend.
- True with a configured destination: those seven route checks reached the controlled Catalog backend.
- Collection paths, item paths, and the query string were preserved.
- Five unrelated route checks stayed on the monolith; gateway health stayed local.
- The applied Catalog endpoints retained the `api` policy and 30-second deadline.
- Enabled with missing or invalid destination configuration: startup was rejected with the intended error.

These are isolated routing checks, not authorization, database, or developer-run gateway results. Record the developer's own results separately:

- [ ] Application changes from Part A are applied.
- [ ] OFF routes to the monolith and existing browsing/login still work.
- [ ] ON with missing configuration fails clearly.
- [ ] ON is verified on all Catalog paths using the reviewed real service and its request logs, or the temporary backend and its marker. Record which backend was tested; a probe pass does not establish real-service readiness.
- [ ] Health, Web login, and Swagger retain their destinations.
- [ ] OFF is restored; temporary probe cluster is removed if used; real monolith responses and login work.

After the applicable gateway checks are complete, continue with Phase 2 Step 2.2: verify and configure native RabbitMQ. The real Catalog switch is completed during Catalog extraction, together with internal caller selection, compatible API/authentication contracts, data migration, and one active Catalog writer. New writes require data reconciliation before a rollback; changing the gateway flag alone is insufficient.
