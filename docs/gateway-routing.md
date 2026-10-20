# Gateway Routing and Migration Controls

**Last updated:** 20 October 2026.  
**Guide:** [Phase 2 walkthrough](../../GatewayAndRabbitMQSetup.md), Step 2.1.7.

## Current routing

The public development address is `https://localhost:7001/`.

| Incoming path | Destination |
| --- | --- |
| `/api/{**remainder}` | PublicApi at `https://localhost:5099/` |
| `/swagger/{**remainder}` | PublicApi at `https://localhost:5099/` |
| Remaining application paths | Web at `https://localhost:5001/` |

The original-host transform preserves the public host/port for backend link and redirect generation. Backend destinations remain the private application addresses, not the gateway address.

## Current access model

YARP forwards requests with their existing cookie/bearer credentials. Web validates customer authentication cookies; PublicApi validates bearer tokens and administrator authorization on product-write endpoints. The gateway currently has no independent authentication policy. Public browsing and login remain accessible.

### Developer-confirmed checks — 20 October 2026

The developer reported that all four walkthrough checks work and supplied the anonymous API response below. These are developer-run checks, not a newly executed agent test suite.

| Check | Expected result | Reported result |
| --- | --- | --- |
| Anonymous catalog browsing through the gateway | Product browsing available | Passed — developer-confirmed |
| Anonymous request to `/Basket/Checkout` | Redirect to login on the gateway address | Passed — developer-confirmed |
| Anonymous POST to `/api/catalog-items` | HTTP 401 | Passed — supplied HTTP response |
| Administrator login and disposable product edit through the gateway | Authorized write succeeds | Passed — developer-confirmed |

```http
HTTP/1.1 401 Unauthorized
Content-Length: 0
Server: Kestrel
WWW-Authenticate: Bearer
```

The supplied response's Date header was `Tue, 20 Oct 2026 16:36:21 GMT`. A 401 is the expected rejection of a request without authentication; it is not a failed access-control test.

Customer-role rejection of administrator writes, invalid-token cases, and resource-ownership checks need their own explicit verification. The original order-detail ownership concern remains tracked in ORD-04; the successful checks above do not represent a correction to that source code.

## Trusted identity during extraction

Before Web calls protected extracted service APIs, define and implement a trusted signed-token/delegation path. Preserve a stable customer ID and validate credentials and resource ownership at each receiving service. A browser-supplied buyer ID or unsigned user-ID header is not trusted authentication. This transition remains planned work before dependent activation.

## Traffic controls — pending implementation and verification

The walkthrough adds an explicit 30-second total request timeout to the PublicApi route using .NET request-timeout middleware. Its test temporarily uses a 100 ms timeout against the existing catalog-list endpoint's one-second delay, then restores 30 seconds and verifies HTTP 200. Run without an attached debugger. Check response timing and gateway cancellation logs as well as the HTTP status.

**Timeout test observation — 20 October 2026:** The developer supplied HTTP 400 with an empty body while testing the 100 ms setting (response Date: `Tue, 20 Oct 2026 16:50:39 GMT`). During that experiment the route used `Timeout: 00:00:00.100`, and the source registered and activated request-timeout middleware. The earlier instruction that this must always return 504 was too strict: [YARP 2.3.0's forwarding code](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L584-L598) can map cancellation signalled through `RequestAborted` to HTTP 400. This is a possible explanation, not a confirmed diagnosis from the status alone. The paired results and the explicit timeout-response walkthrough below provide the follow-up. The normal 30-second route setting was restored after the experiment.

**Measured follow-up:** The developer reported `HTTP 400, time 0.128659s` with the 100 ms setting, then `HTTP 200, time 1.237160s` after restoring the 30-second setting. The saved route was also checked and contained `Timeout: 00:00:30`. These historical paired results supported timeout-induced cancellation; they did not prove an HTTP 504 response. The subsequent middleware implementation and developer-confirmed HTTP 504/200 checks are recorded below.

The API and login rate-limit checks below have passed. Keep normal CSS, images, and Blazor asset loading outside the API allowance. Gateway load balancing remains a later configuration/test when a cluster has multiple actual instances.

### Timeout response walkthrough — developer-confirmed verification

The target is HTTP 504 when the gateway's route deadline expires before the forwarded response starts. ASP.NET Core exposes the deadline's own cancellation token through `IHttpRequestTimeoutFeature.RequestTimeoutToken`. Capture that token after `UseRequestTimeouts`, let forwarding finish, and inspect YARP's forwarding error. Replace the response only when the deadline expired, YARP reported request/request-body cancellation, and the response has not started. A response that has already started cannot have its HTTP status replaced.

This preserves ordinary backend HTTP 400 responses and connection-failure HTTP 502 responses. A client cancellation alone does not satisfy the gateway-deadline check. Keep `app.MapReverseProxy()` unchanged so YARP's default proxy pipeline remains in place. See [ASP.NET Core request timeouts](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-8.0) and [the timeout token API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.timeouts.ihttprequesttimeoutfeature.requesttimeouttoken?view=aspnetcore-8.0).

**Isolated verification — 20 October 2026:** A .NET 8 / YARP 2.3 in-memory forwarding probe reproduced the earlier behavior without the middleware: HTTP 400 in 116 ms for a one-second endpoint with a 100 ms route deadline. With the middleware below, the same check returned HTTP 504 in 98 ms. Both runs preserved a controlled backend 400, mapped a simulated connection failure to 502, returned health 200, and returned 200 from the slow endpoint after restoring the route deadline to 30 seconds. A client cancellation before the deadline was not relabelled as a gateway timeout. These are separate controlled probe results; the developer-run application results are recorded below.

**Why setting `TimeoutStatusCode` alone is insufficient here:** `RequestTimeoutPolicy.TimeoutStatusCode` is supported, and its fallback is already 504. The timeout middleware applies that response when it catches an unhandled timeout-related `OperationCanceledException`. YARP catches the forwarding cancellation and produces its own 400 response, so that catch block is not reached. See the [ASP.NET Core timeout catch block](https://github.com/dotnet/aspnetcore/blob/v8.0.0/src/Http/Http/src/Timeouts/RequestTimeoutsMiddleware.cs#L100-L118) and [YARP's cancellation handling](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L584-L598). A follow-up isolated test explicitly registered `TimeoutStatusCode = StatusCodes.Status504GatewayTimeout` in a named 100 ms policy and selected it on the proxy route using `TimeoutPolicy`, with the route's `Timeout` removed. A direct cancellation-aware endpoint returned 504; the proxied slow endpoint still returned 400. The proposed response-handling middleware remains necessary for this tested YARP 2.3 cancellation path. No switch to the alternative named-policy configuration has been applied to the developer's gateway.

**Developer-confirmed results — 20 October 2026:** After adding the middleware and setting the `public-api` route's deadline to `00:00:00.100`, the developer reported HTTP 504 from the proxied catalog request. After restoring that route to `00:00:30`, the request worked again with HTTP 200. Source inspection confirms the middleware is present and all three configured route timeouts (`public-api`, `api-login`, and `web-login`) are back at 30 seconds. New elapsed-time measurements and the custom timeout-warning output were not supplied; the middleware contains the warning with method, path, and trace ID. Application results are developer-reported, not an independently executed agent test suite.

Repeatable walkthrough:

1. Stop the gateway with Ctrl+C; keep Web and PublicApi running.
2. Add `using Microsoft.AspNetCore.Http.Timeouts;` and `using Yarp.ReverseProxy.Forwarder;` to `src/ApiGateway/Program.cs`, keeping the existing rate-limiting using.
3. Replace the section from `app.UseRouting();` through `app.Run();` with the following. Keep `public partial class Program { }` afterward and retain the earlier service/rate-policy registrations.

```csharp
app.UseRouting();
app.UseRateLimiter();
app.UseRequestTimeouts();

app.Use(async (context, next) =>
{
    var timeoutToken = context.Features
        .Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken
        ?? CancellationToken.None;

    await next(context);

    var proxyError = context.Features.Get<IForwarderErrorFeature>();

    if (timeoutToken.IsCancellationRequested
        && proxyError?.Error is ForwarderError.RequestCanceled
            or ForwarderError.RequestBodyCanceled
        && !context.Response.HasStarted)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;

        app.Logger.LogWarning(
            "Gateway timeout for {Method} {Path}. TraceId: {TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);
    }
});

app.MapGet("/health/gateway", () => Results.Ok(new { status = "Running" }));
app.MapReverseProxy();

app.Run();
```

4. In `src/ApiGateway/appsettings.json`, temporarily set only the `public-api` route's `Timeout` to `00:00:00.100`. Keep the login routes at 30 seconds and preserve all other settings. The existing catalog-list endpoint contains a one-second delay, so this request should exceed the temporary deadline.
5. In the existing gateway PowerShell window, from the repository folder, build and then run without a debugger:

```powershell
dotnet build src/ApiGateway/ApiGateway.csproj --no-restore
dotnet run --project src/ApiGateway/ApiGateway.csproj --no-launch-profile --urls 'https://localhost:7001' --no-build --no-restore
```

6. In the testing PowerShell window, request the catalog endpoint and gateway health:

```powershell
curl.exe -sS --max-time 35 -o NUL -w "HTTP %{http_code}, time %{time_total}s\n" 'https://localhost:7001/api/catalog-items?pageSize=1&pageIndex=0'
curl.exe -sS -o NUL -w "Gateway health: %{http_code}\n" 'https://localhost:7001/health/gateway'
```

Expect HTTP 504 for the catalog request near the 100 ms deadline, allowing startup/connection overhead, and health 200. Save the new `Gateway timeout for GET /api/catalog-items. TraceId: ...` warning. YARP may also log its underlying cancellation warning; the browser-facing status is now 504.

7. Stop the gateway, restore the `public-api` route's `Timeout` to `00:00:30`, rebuild, and restart with the same commands from step 5. Rebuilding copies the restored configuration into the build output. Repeat the requests from step 6 and expect catalog 200 and health 200. Record actual statuses, elapsed times, and the timeout warning before marking explicit timeout handling complete. Preserve API allowance 60 and login allowance 10.

### API rate limit — developer-confirmed verification

The walkthrough introduces a named `api` policy, selected only by the PublicApi route's `RateLimiterPolicy`. It uses a fixed one-minute window, a per-client-IP partition, no request queue, and HTTP 429 on rejection. The normal default is 60 requests per minute; `Gateway:RateLimiting:ApiPermitLimit` can override it. `Gateway__RateLimiting__ApiPermitLimit=3` is the temporary PowerShell setting for the five-request test.

**Developer-confirmed results — 20 October 2026:** With the temporary limit of 3, five requests returned `200, 200, 200, 429, 429`. After changing the limit to 60 and restarting the gateway as instructed, all five returned `200`. This verifies rejection at the test allowance and acceptance of five requests at the restored normal allowance. It does not test exhausting the full allowance of 60. These are developer-run results, not a newly executed agent test suite.

An isolated .NET 8 / YARP 2.3 test-server check of the middleware and route selection also produced API statuses `200, 200, 200, 429, 429` with a limit of 3. The proxied website and gateway health endpoint both remained HTTP 200 after the API allowance was exhausted. This was a separate in-memory probe; those asset/health exclusions have not been independently exercised against the developer's running gateway.

### Login rate limit — developer-confirmed verification

A separate named `login` policy uses a shared per-client-IP allowance of 10 POST submissions per fixed one-minute window across both login endpoints. All submissions count, including successful ones; there is no queue, and rejection returns HTTP 429. The allowance is configured with `Gateway:RateLimiting:LoginPermitLimit`; nonpositive values are rejected at startup. The walkthrough temporarily uses `Gateway__RateLimiting__LoginPermitLimit=3` for testing, then restores 10 and restarts the gateway. The existing API allowance remains 60.

The saved configuration contains these two routes with `Order: -10`, `Timeout: 00:00:30`, `RateLimiterPolicy: login`, `Match.Methods: ["POST"]`, and the existing original-host transform:

| Route | Path | Cluster |
| --- | --- | --- |
| `api-login` | `/api/authenticate` | `monolith-api` |
| `web-login` | `/Identity/Account/Login` | `monolith-web` |

The explicit routes take precedence over the broader API/Web routes. Login POST requests use the stricter `login` policy in place of the route-level `api` policy. GET requests to the Web login page continue through the ordinary Web route. The shared login allowance is separate from the allowance for ordinary API requests. These counters belong to one gateway process and reset when it restarts; multiple gateway instances would need a separate design for a shared limit.

**Developer-confirmed results — 20 October 2026:** The developer reported that the walkthrough produced the expected results: with login allowance 3, API login POST requests returned `200, 200, 200, 429, 429`; the subsequent Web login POST returned 429, while the login page and catalog requests remained 200. After following the restoration to allowance 10, normal login worked. The developer also reported a 429 after submitting ten incorrect logins. The exact request-by-request count for that additional check was not supplied. The allowance counts successful and unsuccessful submissions together; a fresh counter permits ten submissions and rejects the eleventh within the same window, so earlier successful login submissions can consume part of that allowance. The source and route configuration were read and match the walkthrough. Runtime results are developer-reported, not a newly executed agent test suite.

**Isolated verification — 20 October 2026:** A .NET 8 / YARP 2.3 in-memory probe with both allowances temporarily set to 3 produced login POST statuses `200, 200, 200, 429, 429`. After login exhaustion, POST to the Web login path returned 429 and GET to that path returned 200. Ordinary API requests retained their separate allowance and produced `200, 200, 200, 429, 429`; website and health requests remained 200. This verifies the route/policy pattern against controlled backends separately from the developer-run application checks.

Repeatable developer-run check: send five API login requests with a newly generated nonexistent username and a dummy password, using temporary allowance 3. The current authentication endpoint returns HTTP 200 with `Result: false` for incorrect credentials; HTTP 200 therefore indicates that the request was processed, not that login succeeded. Expect the first three requests to reach that endpoint and the last two to return 429. Immediately check that a Web login POST also returns 429, while GET of the login page and ordinary catalog browsing remain available. Restore login allowance 10, restart, and verify normal customer and administrator login through the gateway.

Reference: [YARP rate limiting](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/rate-limiting?view=aspnetcore-10.0) and [route matching configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files).

### Backend unavailable and recovery — developer-confirmed verification

Exercise a controlled PublicApi outage while keeping YARP, Web, and SQL LocalDB running. Leave configuration and rate-limit allowances unchanged.

**Developer-confirmed results — 20 October 2026:** The developer reported that all walkthrough steps produced the expected results and supplied the gateway warning below. These are developer-run results, not a newly executed agent test suite. Exact response timings were not supplied.

| Check | Reported result |
| --- | --- |
| Proxied catalog request before stopping PublicApi | HTTP 200 |
| Proxied catalog request while PublicApi was stopped | HTTP 502 |
| Gateway health while PublicApi was stopped | HTTP 200 |
| Web login page while PublicApi was stopped | HTTP 200 |
| Proxied catalog request after restarting PublicApi, without restarting YARP | HTTP 200 |

Relevant excerpt from the supplied gateway log:

```text
warn: Yarp.ReverseProxy.Forwarder.HttpForwarder[48]
      Request: An error was encountered before receiving a response.
      System.Net.Http.HttpRequestException: Der kunne ikke oprettes forbindelse, fordi destinationscomputeren aktivt nægtede det. (localhost:5099)
       ---> System.Net.Sockets.SocketException (10061): Der kunne ikke oprettes forbindelse, fordi destinationscomputeren aktivt nægtede det.
```

Windows socket error 10061 means connection refused, consistent with the deliberately stopped API listener. See [Microsoft's Windows socket error reference](https://learn.microsoft.com/en-us/windows/win32/winsock/windows-sockets-error-codes-2#wsaeconnrefused). The warning is the expected diagnostic for this outage exercise. This confirms failure reporting, continued gateway/Web availability for the checked routes, and recovery after API restart. Slow-request timeout handling has its own verification recorded above.

Repeatable walkthrough:

1. With all hosts running, verify HTTP 200 for the proxied catalog endpoint and `/health/gateway`.
2. In the PowerShell window running PublicApi at `https://localhost:5099`, press Ctrl+C and wait for the prompt to return. Keep that window open so its process-scoped database and URL settings remain available for the restart.
3. Request `https://localhost:7001/api/catalog-items?pageSize=1&pageIndex=0` again. With the current single-destination configuration, expect HTTP 502 when YARP cannot connect to the stopped API. Record the status, elapsed time, and the related gateway connection-failure log. A browser-facing response body may be empty. [YARP 2.3.0 forwarding failure handling](https://github.com/dotnet/yarp/blob/v2.3.0/src/ReverseProxy/Forwarder/HttpForwarder.cs#L620-L627) maps a destination communication failure to 502; this is distinct from waiting for a slow backend to time out.
4. While PublicApi remains stopped, verify HTTP 200 for `/health/gateway` and GET `/Identity/Account/Login` through port 7001. These demonstrate gateway liveness and availability of that Web route respectively. Gateway liveness alone does not demonstrate backend readiness.
5. In the same PublicApi PowerShell window, from the repository directory, run `dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi --no-build --no-restore`. Wait for startup/seeding to finish and for the HTTPS listener on port 5099.
6. Request the proxied catalog endpoint again and expect HTTP 200 without restarting YARP. Verify gateway health remains 200.

Use a test-terminal request such as:

```powershell
curl.exe -sS --max-time 35 -o NUL -w "HTTP %{http_code}, time %{time_total}s\n" 'https://localhost:7001/api/catalog-items?pageSize=1&pageIndex=0'
```

Expected catalog sequence is `200 -> 502 -> 200`, while gateway health remains 200, matching the developer-confirmed results above. If a repeat outage request returns 200, verify that PublicApi really stopped and that no second process is serving port 5099; if it returns 429, allow the existing API rate-limit window to replenish before repeating. Explicit timeout response handling has separate recorded verification above.

## Migration controls — pending implementation

Add per-domain route selection, beginning with Catalog routed to the monolith by default. Test both paths with controlled destinations and reject an enabled service route with missing configuration. Coordinate real activation with Web/PublicApi's internal local/API selection and the data cutover. After new writes, reverting a flag alone is not a complete rollback.

Follow [Catalog migration toggle — Step 2.1.7](catalog-migration-toggle.md) for the exact application edits and tests. Part A adds the selector and verifies OFF. Part B checks the missing-destination guard, verifies ON using the reviewed real service or a temporary routing-test backend, and restores OFF. The code passed isolated routing checks; developer implementation and runtime verification remain pending.

**Team update — 20 October 2026:** New Catalog work is present on remote `main`, ahead of the local checkout. Follow the guide's [team handoff](catalog-migration-toggle.md#team-handoff--catalog-work-is-already-on-main): preserve the uncommitted gateway work, integrate main, review Catalog readiness, and use the real service for the enabled-path check once its remaining authorization and integration checks pass. The temporary backend remains an optional routing test. No real Catalog activation has been verified.

Walkthrough scope:

1. Define a gateway Catalog routing flag, disabled by default. Read it at startup and require an intentional gateway restart when changing it.
2. Give `/api/catalog-items` (including item paths), `/api/catalog-brands`, and `/api/catalog-types` explicit routes that retain the existing API allowance, 30-second deadline, and original-host transform. Login and other routes retain their existing destinations.
3. Make the flag select the Catalog routes' destination: monolith PublicApi when disabled, the configured Catalog service when enabled. Validate an enabled destination's configuration and fail clearly when it is missing or invalid. A syntactically valid address alone does not prove a service is ready.
4. Test disabled/default routing, enabled routing against a controlled test backend, unrelated routes, and invalid enabled configuration. Keep business traffic routed to the monolith afterward. This preparation does not activate the existing Catalog prototype or migrate catalog data.
5. During Catalog extraction, add the internal caller switches and coordinate activation with data transfer and one active catalog writer. Web's internal repository calls do not pass through YARP.

The flag and route selection are not implemented yet. After the applicable gateway controls are complete, continue Phase 2 with native RabbitMQ setup (Step 2.2 onward).

## Step 2.1.7 progress

- [x] Initial access checks through YARP passed — developer-confirmed 20 October 2026.
- [x] Initial access behavior and supplied HTTP result recorded.
- [ ] Trusted identity path implemented before protected service activation.
- [x] Explicit API timeout configured; developer's paired timing results recorded and normal 30-second setting restored.
- [x] Explicit HTTP 504 deadline response and restored HTTP 200 verified — developer-confirmed 20 October 2026; cancellation-aware handler and diagnostic logging checked in source.
- [x] API rate limiting implemented and tested with allowance 3; normal allowance 60 restored — developer-confirmed 20 October 2026.
- [x] Stricter login rate limits implemented and tested; normal allowance 10 restored and normal login works — developer-confirmed 20 October 2026.
- [x] Backend-unavailable behavior and recovery checked; connection-refused warning recorded — developer-confirmed 20 October 2026.
- [ ] Migration flags select real routes; disabled/default and invalid-configuration paths are tested.
- [ ] Multi-instance load balancing configured/tested when applicable.

Step 2.1.7 remains in progress until the applicable controls and tests are complete.
