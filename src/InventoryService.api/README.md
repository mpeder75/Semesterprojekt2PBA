# Inventory service

Inventory owns stock quantities and order reservations in its own SQL Server database. It provides explicit creation, lookup, updating, adjustment, deletion, reservation and release through HTTP. The application starts independently of Catalog, Order and RabbitMQ.

## Architecture

The service uses a folder-based onion-style structure within one project. Business operations depend on interfaces; the database implementation sits outside those operations.

| Folder | Responsibility |
| --- | --- |
| `Entities` | Domain objects and quantity/reservation rules; no EF Core or HTTP dependencies |
| `Interfaces` | Application contracts: `IStockService`, `IInventoryRepository`, and `IInventoryTransaction` |
| `Services` | Stock and reservation workflows, implemented by `StockService` using the repository contract |
| `Repositories` | `InventoryRepository`, `InventoryDbContext`, EF/LINQ queries, optimistic concurrency, transaction/save handling, and persistence registration |
| `Repositories/Migrations` | Existing EF migrations and the model snapshot |
| `Endpoints` and `Models` | HTTP routing, request validation, response DTOs, and mapping |

Endpoints request `IStockService`. `StockService` depends on `IInventoryRepository` and domain entities. `InventoryRepository` implements that inner contract using EF Core. The domain, contracts and service contain no database queries or EF Core references. `Program.cs` is the composition root: it connects the interfaces to their implementations and calls the repository startup migration helper.

The application service defines which changes form one operation through `IInventoryTransaction`. The repository uses EF for queries, change tracking, saving and transaction handling. An abandoned transaction also clears pending EF changes, allowing the repository to be reused safely within the same scope. There is no generic repository base or Ardalis dependency in Inventory.

These layer boundaries are expressed through folders, namespaces and interfaces within the single project; separate assemblies do not enforce them. Moving the existing migrations into `Repositories/Migrations` preserves their IDs and database schema.

## Run locally

Run these commands from the `Semesterprojekt2PBA` directory. Install the .NET 8 SDK and SQL Server LocalDB, as used by the existing services.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/InventoryService.api/InventoryService.api.csproj --no-launch-profile --urls http://localhost:57967
```

The default connection uses Windows authentication and the dedicated `InventoryDb` database on `(localdb)\MSSQLLocalDB`. Startup creates the database and applies its EF migrations. It adds no sample stock and preserves existing quantities on subsequent starts. Database startup errors stop the application.

To use another SQL Server, supply `ConnectionStrings__InventoryConnection` through the environment or user secrets. Keep passwords out of tracked files. The account needs permission to create the database/apply migrations for this local startup workflow.

Swagger is available in Development at `http://localhost:57967/swagger`. The service can also be reached through the gateway after enabling its Inventory flag.

## Run through the gateway

Keep Inventory running on port 57967. In a second terminal at the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:FeatureManagement__UseInventoryService = 'true'
dotnet run --project src/ApiGateway --no-launch-profile -- --urls 'https://localhost:7153'
```

Use `https://localhost:7153/api/inventory` for the API operations below; the gateway forwards their paths, methods and bodies unchanged. For example, create with `POST https://localhost:7153/api/inventory` and read with `GET https://localhost:7153/api/inventory/1`. Trust the local ASP.NET development certificate when using HTTPS, as described in the [gateway README](../ApiGateway/README.md#run-locally).

The shared flag defaults to false; disabled Inventory routes return 503 Problem Details and do not reach any backend. Restart the gateway after changing the flag. Enabled Inventory routes use the existing API rate limit (60 requests per minute per client), two-minute deadline, and an independent `inventory-service` circuit. A 409 business conflict does not open that circuit.

Production keeps Inventory disabled with no destination configured. A deployed Inventory URL must be configured before enabling it. This gateway change does not deploy the service or add RabbitMQ/Order messaging.

## HTTP API

| Request | Behavior |
| --- | --- |
| `POST /api/inventory` with `{ "catalogItemId": 1, "quantity": 10 }` | Create stock: `201` and a Location header; `409` when already initialized |
| `GET /api/inventory/{catalogItemId}` | Read stock: `200`; `404` for uninitialized stock |
| `PUT /api/inventory/{catalogItemId}` with `{ "quantity": 10 }` | Set on-hand quantity: `200`; `404` when missing; `409` below reserved quantity |
| `POST /api/inventory/{catalogItemId}/adjustments` with `{ "quantityChange": -2 }` | Increase/decrease on-hand stock: `200`; `404` when missing; `409` if reserved units would be consumed |
| `DELETE /api/inventory/{catalogItemId}` | Delete zero-stock records without reserved units: `204`; `404` when missing; `409` otherwise |
| `POST /api/inventory/reservations` | Reserve all order items: `201`; identical replay `200`; insufficient stock or a changed order payload `409`; uninitialized product `404` |
| `GET /api/inventory/reservations/{orderId}` | Read a reservation and its lines: `200`; `404` when missing |
| `POST /api/inventory/reservations/{orderId}/confirm` | Confirm an existing allocation: `200`, including repeated confirmation; `404` when missing; `409` when released |
| `POST /api/inventory/reservations/{orderId}/release` | Release reserved units: `200`, including repeated release; `404` when missing |

Stock responses use `{ "catalogItemId": 1, "quantity": 10, "reservedQuantity": 3, "availableQuantity": 7 }`. `quantity` means on-hand stock; availability is on-hand minus reserved. Reserving and releasing do not reduce on-hand stock.

Product/order IDs must be positive integers. Initial/set quantity is required and must be an integer from zero through `2147483647`. Adjustments must be non-zero and cannot overflow that range. Invalid inputs or malformed JSON return `400`. Missing stock is distinct from an initialized quantity of zero.

HTTP contracts are separate models in `Models/Requests` and `Models/Responses`. Response models are DTOs; endpoints map the internal entities to them.

Example:

```powershell
Invoke-RestMethod -Method Post -Uri 'http://localhost:57967/api/inventory' -ContentType 'application/json' -Body '{"catalogItemId":1,"quantity":10}'
Invoke-RestMethod -Method Post -Uri 'http://localhost:57967/api/inventory/reservations' -ContentType 'application/json' -Body '{"orderId":42,"items":[{"catalogItemId":1,"quantity":3}]}'
Invoke-RestMethod -Uri 'http://localhost:57967/api/inventory/1'
Invoke-RestMethod -Method Post -Uri 'http://localhost:57967/api/inventory/reservations/42/release'
```

`POST` creates a stock record; `PUT` only updates an existing record. Both setting and decreasing stock protect reserved units. Explicit adjustments are ordinary non-idempotent HTTP operations: sending the same adjustment twice applies it twice. Use order reservations for allocation, rather than also deducting the same order through adjustments.

## Reservation lifecycle and service ownership

A reservation belongs to one positive integer `orderId`, matching Order's existing ID type, and contains one positive quantity per distinct product. All lines succeed together or no stock/reservation changes are saved. Retrying the same order and lines returns its stored reservation; changing the lines returns `409`. Item order does not affect duplicate detection. Replaying a released reservation returns `Released` without reserving again. A new order needs a new ID.

A reservation starts as `Reserved`. Confirmation changes it to `Confirmed` and keeps all stock quantities unchanged. Explicit release works for either active status and returns the units to availability. A released reservation cannot be confirmed. Released reservations are retained to prevent delayed retries from allocating again, including after restart.

Payment, shipping/fulfillment, automatic expiry and automatic changes to Order statuses are outside this standalone service.

EF-managed read-committed transactions keep multi-item reservations/releases atomic. All repository reads use LINQ; there are no handwritten locking queries. `InventoryItem` and `StockReservation` each have a SQL Server-generated eight-byte `RowVersion`, configured with EF's `IsRowVersion()`. EF checks the original version when updating/deleting a row and raises a concurrency exception if another request changed it. Database check constraints also enforce valid quantity bounds.

A failed save rolls back both stock and reservation changes and clears EF tracking. The repository translates stale-version failures and concurrent duplicate-ID inserts into an application concurrency exception. The service reloads and retries the complete operation, up to five attempts; exhausted contention returns HTTP 409. Concurrent reservation/release replays also recheck the stored order outcome before rejecting already-allocated/released units.

Only confirmed pre-commit conflicts are retried. Uncertain commit failures and unrelated database failures are not automatically replayed. Ordinary HTTP adjustments therefore still apply again if a caller submits them a second time. `RowVersion` is internal persistence metadata; HTTP request/response shapes are unchanged and do not implement client-provided version preconditions or ETags.

The `AddRowVersion` migration adds the tokens to existing stock and reservation rows without resetting quantities or allocations. Startup applies this migration automatically. Migration preservation is covered by a test that upgrades a populated pre-RowVersion database.

Catalog and Order IDs are references supplied by callers. Inventory has no foreign keys into either service's database, no shared entities, and no HTTP dependency on those services. It does not verify that their IDs exist. The relationship from `ReservationItems` to `Reservations` is internal to Inventory's own database. A Catalog product is initialized explicitly through Inventory's create endpoint.

The API has the same deferred-auth scope as the other extracted service APIs. Gateway routing is implemented; RabbitMQ and automatic Order integration remain separate later tasks.

## Tests

```powershell
dotnet test tests/InventoryService.Tests/InventoryService.Tests.csproj
dotnet test tests/IntegrationTests/IntegrationTests.csproj --filter 'FullyQualifiedName~Microsoft.eShopWeb.IntegrationTests.InventoryService'
```

Unit tests require no database or broker. They cover initialization, setting quantities, increases/decreases, reservations, releases, insufficient stock, invalid inputs, overflow, and preserving quantities after a rejected change. Service tests substitute `IInventoryRepository` to verify allocation/release workflows, replay handling, rejected operations, safe conflict retries and uncertain-commit handling independently of EF Core.

Inventory integration tests live in `tests/IntegrationTests/InventoryService`, alongside the existing Basket and Order repository tests in the shared `IntegrationTests` project. Run the command above for Inventory alone, or omit the filter to run all integration tests.

Integration tests send real HTTP requests through `WebApplicationFactory` and use the real EF/SQL Server provider and migrations. They cover CRUD, adjustments, reservations/releases, replay after restart, concurrent orders and duplicate requests, database constraints, and rollback after both business rejection and forced SQL write failure. Each fixture creates a database named `InventoryTests_<random GUID>` and drops only that database afterward. SQL Server must be available; missing infrastructure fails the suite rather than skipping it.

`InventoryGatewayIntegrationTests` additionally runs the actual gateway and Inventory apps in-process, connecting YARP's outgoing HTTP transport to Inventory's TestServer. It verifies create/read/adjust/reserve/replay/conflict/release/update/delete through the gateway against SQL Server. No Azure resources or external backend listeners are used.

`InventoryConcurrencyTests` verifies generated/changing version tokens, stale stock updates/deletes, stale reservation writes, and migration of existing stock/reservations. Repository SQL statements in the production service have been removed; test-only SQL remains where a test deliberately constructs an old schema or database failure.

Tests default to Windows-authenticated LocalDB. For another test SQL Server, set `INVENTORY_TEST_SQL` to a connection string for that server/account. The fixture always replaces its database name with a fresh test-owned name. Use an account allowed to create and drop test databases. No RabbitMQ connection is needed for these tests.

The implementation was developed with failing stock-rule tests first, followed by failing HTTP tests, then the corresponding code.
