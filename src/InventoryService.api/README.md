# Inventory service

Inventory owns stock quantities and order reservations in its own SQL Server database. It provides explicit creation, lookup, updating, adjustment, deletion, reservation and release through HTTP. The application starts independently of Catalog, Order and RabbitMQ.

## Architecture

The service uses a folder-based onion-style structure within one project. Business operations depend on interfaces; the database implementation sits outside those operations.

| Folder | Responsibility |
| --- | --- |
| `Entities` | Domain objects and quantity/reservation rules; no EF Core or HTTP dependencies |
| `Interfaces` | Application contracts: `IStockService`, `IInventoryRepository`, and `IInventoryTransaction` |
| `Services` | Stock and reservation workflows, implemented by `StockService` using the repository contract |
| `Repositories` | `InventoryRepository`, `InventoryDbContext`, SQL queries/locks, transaction/save handling, and persistence registration |
| `Repositories/Migrations` | Existing EF migrations and the model snapshot |
| `Endpoints` and `Models` | HTTP routing, request validation, response DTOs, and mapping |

Endpoints request `IStockService`. `StockService` depends on `IInventoryRepository` and domain entities. `InventoryRepository` implements that inner contract using EF Core. The domain, contracts and service contain no database queries or EF Core references. `Program.cs` is the composition root: it connects the interfaces to their implementations and calls the repository startup migration helper.

The application service defines which changes form one operation through `IInventoryTransaction`. The repository handles the SQL transaction, locking, saving and rollback. An abandoned transaction also clears pending EF changes, allowing the repository to be reused safely within the same scope. There is no generic repository base or Ardalis dependency in Inventory.

These layer boundaries are expressed through folders, namespaces and interfaces within the single project; separate assemblies do not enforce them. Moving the existing migrations into `Repositories/Migrations` preserves their IDs and database schema.

## Run locally

Run these commands from the `Semesterprojekt2PBA` directory. Install the .NET 8 SDK and SQL Server LocalDB, as used by the existing services.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/InventoryService.api/InventoryService.api.csproj --no-launch-profile --urls http://localhost:57967
```

The default connection uses Windows authentication and the dedicated `InventoryDb` database on `(localdb)\MSSQLLocalDB`. Startup creates the database and applies its EF migrations. It adds no sample stock and preserves existing quantities on subsequent starts. Database startup errors stop the application.

To use another SQL Server, supply `ConnectionStrings__InventoryConnection` through the environment or user secrets. Keep passwords out of tracked files. The account needs permission to create the database/apply migrations for this local startup workflow.

Swagger is available in Development at `http://localhost:57967/swagger`. Call the service directly on this port; Inventory has not yet been added to the gateway.

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

A reservation belongs to one positive integer `orderId`, matching Order's existing ID type, and contains one positive quantity per distinct product. All lines succeed together or no stock/reservation changes are saved. Reservation status is `Reserved` or `Released`. Retrying the same order and lines returns its stored reservation; changing the lines returns `409`. Item order does not affect duplicate detection. Replaying a released reservation returns `Released` without reserving again. A new order needs a new ID.

There is no payment or expiration step. A confirmed order can retain its allocation until cancellation calls release. Payment, shipping/fulfillment, automatic expiry and automatic changes to Order statuses are outside this standalone service. Released reservations are retained to prevent delayed retries from allocating again, including after restart.

SQL transactions and update locks protect all stock writers, order uniqueness and multi-item reservations/releases. Database check constraints enforce valid quantity bounds. A failed transaction rolls back both the stock changes and reservation data. Automatic database retries are disabled so an adjustment is not silently repeated if a commit outcome is uncertain. Reservations/releases can be explicitly retried with the same order ID.

Catalog and Order IDs are references supplied by callers. Inventory has no foreign keys into either service's database, no shared entities, and no HTTP dependency on those services. It does not verify that their IDs exist. The relationship from `ReservationItems` to `Reservations` is internal to Inventory's own database. A Catalog product is initialized explicitly through Inventory's create endpoint.

The API has the same deferred-auth scope as the other extracted service APIs. RabbitMQ, gateway routing, and automatic Order integration remain separate later tasks.

## Tests

```powershell
dotnet test tests/InventoryService.Tests/InventoryService.Tests.csproj
dotnet test tests/IntegrationTests/IntegrationTests.csproj --filter 'FullyQualifiedName~Microsoft.eShopWeb.IntegrationTests.InventoryService'
```

Unit tests require no database or broker. They cover initialization, setting quantities, increases/decreases, reservations, releases, insufficient stock, invalid inputs, overflow, and preserving quantities after a rejected change. Service tests substitute `IInventoryRepository` to verify allocation/release workflows, replay handling and rejected operations independently of EF Core.

Inventory integration tests live in `tests/IntegrationTests/InventoryService`, alongside the existing Basket and Order repository tests in the shared `IntegrationTests` project. Run the command above for Inventory alone, or omit the filter to run all integration tests.

Integration tests send real HTTP requests through `WebApplicationFactory` and use the real EF/SQL Server provider and migrations. They cover CRUD, adjustments, reservations/releases, replay after restart, concurrent orders and duplicate requests, database constraints, and rollback after both business rejection and forced SQL write failure. Each fixture creates a database named `InventoryTests_<random GUID>` and drops only that database afterward. SQL Server must be available; missing infrastructure fails the suite rather than skipping it.

Tests default to Windows-authenticated LocalDB. For another test SQL Server, set `INVENTORY_TEST_SQL` to a connection string for that server/account. The fixture always replaces its database name with a fresh test-owned name. Use an account allowed to create and drop test databases. No RabbitMQ connection is needed for these tests.

The implementation was developed with failing stock-rule tests first, followed by failing HTTP tests, then the corresponding code. See the workspace's `InventoryServiceImplementationPlan.md` for recorded checkpoints and the later RabbitMQ phase.
