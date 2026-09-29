# Catalog microservice and feature toggle

## What is implemented

Catalog now has its own runnable .NET 8 project, `src/Catalog.Api`, its own data model, and its own SQL Server database. It supports product reads, creation, updates, deletion, brands, and types. Web and PublicApi can switch all catalog access between the legacy repositories and the microservice.

The service references only `Catalog.Contracts`, a small project containing DTOs and input validation. It does not reference Web, ApplicationCore, Infrastructure, Identity, baskets, or orders. Both solution files include the new projects and their tests.

```text
Web: browsing / baskets / checkout / Razor admin
PublicApi: existing routes used by Blazor admin
                       |
                 ICatalogClient
                       |
           FeatureFlags:UseCatalogApi
                /                \
             false               true
               |                   |
    RepositoryCatalogClient   HttpCatalogClient
               |                   | HTTP + service key
     Legacy catalog tables     Catalog.Api
     in monolith database          |
                            CatalogService database
                            Items / Brands / Types
```

Baskets, orders, identity, and product image files remain in the existing application. Orders continue storing product snapshots, so later catalog edits do not rewrite order history. The service returns image paths; Web/PublicApi continue composing browser-facing URLs.

## Why these changes were made

| Change | Reason |
| --- | --- |
| Added `Catalog.Api` with its own `Program.cs`, data context, configuration, and Dockerfile | Catalog can start, build, and deploy independently. Its schema contains only catalog data. |
| Moved DTOs into `Catalog.Contracts` | Defines the HTTP data contract without sharing persistence entities or the monolith implementation. Contract changes still require compatibility between deployed versions. |
| Expanded `ICatalogClient` to reads and writes | A single switch selects one backend for browsing, basket lookups, checkout, and administration. Separate read/write flags could accidentally send edits and reads to different databases. |
| Added `HttpCatalogClient` and `CatalogRegistration` in Infrastructure | Keeps HTTP details outside the UI and domain services. Uses a configurable timeout and translates service failure into an explicit unavailable error. |
| Updated all catalog consumers, including PublicApi endpoints | Existing URLs and admin UI response shapes remain usable. Blazor continues calling PublicApi, which delegates to the selected catalog backend. |
| Kept `RepositoryCatalogClient` and legacy catalog entities/tables | Provides the OFF path during migration. Removing them now would make the toggle meaningless. When ON, application catalog operations no longer access those tables. |
| Skip legacy catalog seeding when ON | Prevents Web/PublicApi from populating an unused catalog. Existing monolith migrations still initialize the combined schema needed for baskets/orders. |
| Disable Web catalog view-model caching when ON | Avoids showing stale cached products after service-side edits. Existing Blazor client caching remains; refresh the admin UI when comparing data after migration. |
| Added deterministic product ordering by ID | Pagination must have the same stable ordering in local and remote implementations. |
| Added offline import/export | Preserves IDs referenced by existing baskets/orders and avoids silently overwriting data. |
| Added tests for toggle selection, HTTP contracts, auth, checkout, and SQL transfer | Verifies both migration paths and the boundaries most likely to break during extraction. |

## Toggle behavior

Web and PublicApi each contain this default configuration:

```json
{
  "FeatureFlags": { "UseCatalogApi": false },
  "CatalogApi": { "BaseUrl": "http://localhost:5300/", "TimeoutSeconds": 5 }
}
```

| Setting | Behavior |
| --- | --- |
| `false` | All catalog reads/writes use legacy repositories. Catalog.Api need not be running. |
| `true` | All catalog reads/writes use HTTP. BaseUrl and ApiKey are required at startup. |

The flag is evaluated during dependency registration at startup. Restart **both Web and PublicApi**, with the same value, to change it. Updating appsettings while a process is running does not switch that process. Use a coordinated cutover while catalog edits are paused; do not leave old and new instances writing to different databases.

Environment variables override appsettings:

```powershell
$env:FeatureFlags__UseCatalogApi = "true"
$env:CatalogApi__BaseUrl = "http://localhost:5300/"
$env:CatalogApi__ApiKey = "<same-secret-as-Catalog.Api>"
$env:CatalogApi__TimeoutSeconds = "5"
```

The client does not retry writes or silently fall back to the legacy database. On connection failure, timeout, or service failure, Web/PublicApi return HTTP 503. A timeout can leave a write outcome uncertain; check the catalog before manually retrying a create. Cancellation supplied by a caller is preserved.

## Run a fresh learning demo locally

Run commands from the repository root. Use three terminals, and put the same randomly generated secret into each terminal's `CatalogApi__ApiKey` environment variable. A value can be generated with `[Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')`; do not commit it.

Terminal 1, Catalog:

```powershell
$env:CatalogApi__ApiKey = "<your-secret>"
$env:UseOnlyInMemoryDatabase = "true"
$env:SeedDemoData = "true"
dotnet run --project src/Catalog.Api --no-launch-profile --urls http://localhost:5300
```

Terminal 2, Web:

```powershell
$env:CatalogApi__ApiKey = "<your-secret>"
$env:FeatureFlags__UseCatalogApi = "true"
$env:CatalogApi__BaseUrl = "http://localhost:5300/"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/Web
```

Terminal 3, PublicApi (needed for Blazor administration):

```powershell
$env:CatalogApi__ApiKey = "<your-secret>"
$env:FeatureFlags__UseCatalogApi = "true"
$env:CatalogApi__BaseUrl = "http://localhost:5300/"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi
```

Keep the existing Web/PublicApi launch profiles and browser-facing `baseUrls` aligned, as before this change. The internal Catalog URL is separate from those browser-facing URLs. If frontend library restoration is blocked in your environment, the C# build/tests can use `-p:LibraryRestore=false`; a full browser demo still needs the existing frontend assets installed.

This demo uses disposable in-memory data. Restarting Catalog resets its products. Its demo IDs match the untouched sample catalog, but demo seeding is **not** a migration of an edited database.

For persistent local storage, set `UseOnlyInMemoryDatabase=false` for Catalog and use `ConnectionStrings__CatalogDatabase`. The default is a dedicated LocalDB database named `Microsoft.eShopOnWeb.CatalogService`, separate from the monolith's `Microsoft.eShopOnWeb.CatalogDb`. `SeedDemoData` defaults to false.

Check readiness with `GET http://localhost:5300/health`.

## API contract

All `/catalog` endpoints require the `X-Catalog-Key` header. `/health` is unauthenticated and checks database connectivity.

| Method and path | Result |
| --- | --- |
| `GET /catalog/items?pageIndex=0&pageSize=10&brandId=2&typeId=1` | `{ items, totalItems }`; count is filtered but not paginated. Omit optional filters. |
| `GET /catalog/items/{id}` | Product DTO, or 404. |
| `POST /catalog/items/lookup` with JSON `[1,2,3]` | Existing matching products, in ID order, without duplicates. Empty input gives an empty list. |
| `GET /catalog/brands` | Brand DTOs. |
| `GET /catalog/types` | Type DTOs. |
| `POST /catalog/items` | Create product; 201. New products use the existing default image. |
| `PUT /catalog/items/{id}` | Update details, price, brand, type; preserves the image; 200 or 404. |
| `DELETE /catalog/items/{id}` | 204 or 404. |

Create/update body:

```json
{ "name": "Example", "description": "Example product", "price": 12.50, "catalogBrandId": 1, "catalogTypeId": 2 }
```

Page indexes start at zero. Page size zero means all matching products, preserving the existing PublicApi convention. Invalid input returns 400; duplicate creates return 409. Names must be nonblank and at most 50 characters; descriptions are required, prices positive, and brand/type IDs must exist. Product uploads remain disabled. Brands/types are readable and transferable; this extraction does not introduce a new brand/type administration interface.

The service key is a server credential, not a browser credential. Existing Web cookie/role checks and PublicApi JWT administrator checks protect user-facing writes. Never give the service key to Blazor/browser clients. Outside a local/private development network, terminate TLS and store the key in deployment secrets. The Catalog database connection belongs only to Catalog; use separate restricted database credentials in a deployed environment.

## Migrate existing SQL catalog data

The service includes offline commands; none starts the HTTP listener. Perform the following during a maintenance window with catalog writers stopped, and take a backup first. Keep the flag OFF until import and verification complete.

```powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
$snapshot = Join-Path $PWD "artifacts/catalog-before-cutover.json"
$verification = Join-Path $PWD "artifacts/catalog-after-import.json"
$env:UseOnlyInMemoryDatabase = "false"
$env:SeedDemoData = "false"
$env:ConnectionStrings__LegacyCatalog = "Server=(localdb)\mssqllocaldb;Database=Microsoft.eShopOnWeb.CatalogDb;Integrated Security=true;TrustServerCertificate=true"
$env:ConnectionStrings__CatalogDatabase = "Server=(localdb)\mssqllocaldb;Database=Microsoft.eShopOnWeb.CatalogService;Integrated Security=true;TrustServerCertificate=true"

dotnet run --project src/Catalog.Api --no-launch-profile -- --export-legacy $snapshot
dotnet run --project src/Catalog.Api --no-launch-profile -- --import $snapshot
dotnet run --project src/Catalog.Api --no-launch-profile -- --export $verification
```

`--export-legacy` only reads the legacy `Catalog`, `CatalogBrands`, and `CatalogTypes` tables. Use a read-only source account if available. Snapshot files contain product data, not connection strings. Export refuses to overwrite an existing file.

Import requires empty target catalog tables, validates IDs/references, and writes the three tables in a SQL transaction. Explicit identity insertion preserves IDs and advances SQL identity values for subsequent creates. It does not touch baskets, orders, identity, or the source database. If the target already contains demo data, choose a new empty target database rather than overwriting it.

Compare the two JSON snapshots and counts. Start Catalog, verify reads, then restart Web and PublicApi with the flag ON and the same service credentials. Verify browsing, adding to a basket, checkout, and admin edits before reopening traffic. Remove the legacy connection variable from the Catalog runtime environment after export.

## Rollback and ownership

Before any service-side writes, if the legacy and service datasets still match, stop traffic, restart both callers with the flag OFF, and resume traffic. Restarting also clears server-side caches.

After service-side writes, the legacy dataset is stale. Do **not** just flip the flag. Stop writes, export the current service data with `--export`, reconcile it into the legacy database while preserving IDs and handling deletions, verify it, and only then switch both callers OFF. Automated reverse migration and live synchronization are not implemented. Repairing the service while keeping it authoritative is often preferable.

The OFF path is retained intentionally for this migration. Once the team no longer needs it, remove the legacy implementation and catalog entities/tables through a separately reviewed database migration. Never drop the old combined `CatalogContext`, because it also owns baskets and orders.

## Docker

The optional override adds Catalog.Api and a separate SQL Server container with a persistent named volume. It also enables the same flag and service key in both existing callers.

```powershell
$env:CATALOG_API_KEY = "<your-secret>"
$env:CATALOG_SQL_PASSWORD = "<strong-local-test-password>"
# Only for a NEW demo database:
$env:CATALOG_SEED_DEMO = "true"
docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.catalog.yml up --build
```

The API is available locally on port 5300. The new SQL container is internal to the Compose network. Catalog restarts on failure while SQL becomes ready. For existing data, leave demo seeding false and import the snapshot before exposing callers to traffic. Back up the named volume; deleting it deletes service data. Docker deployment itself must be verified on a machine with a running Docker engine.

## Validation and limitations

Verified on this machine: **88 tests passed** (45 unit, 6 integration, 12 functional, 15 PublicApi integration, and 10 Catalog service tests). The real SQL smoke test passed, including snapshot equality, ID preservation, new identity values, and persistence after restarting the API. Compose configuration validation and `git diff --check` passed. Container build/start was not tested because the Docker engine was not running.

Tests are in `tests/CatalogServiceTests`, plus the earlier unit/integration regression tests. They cover both toggle paths, CRUD, filtering/pagination, missing IDs, authorization, failure without fallback, ID-preserving snapshots, storefront rendering, basket mapping, checkout snapshots, and PublicApi writes while the monolith's catalog tables are empty.

```powershell
dotnet test Everything.sln --no-restore -p:LibraryRestore=false -m:1
dotnet build src/Catalog.Api/Catalog.Api.csproj
./scripts/Test-CatalogSql.ps1
```

The SQL smoke script creates uniquely named temporary databases, exports a legacy catalog, imports it, checks identical snapshots and generated IDs, starts the real API, verifies data survives a process restart, and drops only its temporary databases. It requires LocalDB/SQL Server, `sqlcmd`, and a prior Debug build. Test logs/snapshots go under ignored `artifacts/`.

Schema bootstrap currently uses EF `EnsureCreated`, suitable for the initial learning deployment. It does not upgrade an existing database schema. Add a deliberate migrations strategy before evolving a deployed catalog schema. The current service is a synchronous extraction: events, live data replication, distributed transactions, and a production identity provider are outside this change.

The repository still has its pre-existing invalid SDK pin (`8.0.x`) in `global.json`; this machine ignores it and uses SDK 10 to build net8.0 projects, with .NET 8 installed to run them. Existing package security warnings are not addressed by this extraction.
