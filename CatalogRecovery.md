# Catalog upgrades and rollback

Run commands from the repository root. Build Catalog first. Use SQL Server storage (`UseOnlyInMemoryDatabase=false`) and disable demo seeding. Connection strings belong in environment variables, not committed files.

## Upgrade a database created before migrations

1. Stop Catalog and all Catalog writers. Back up the database.
2. With `ConnectionStrings__CatalogDatabase` pointing to the existing database, export:

   ```powershell
   dotnet run --project src/Catalog.Api --no-launch-profile -- --export artifacts/catalog-before-upgrade.json
   ```

3. Point that variable to a **new, empty database**, then import and export again:

   ```powershell
   dotnet run --project src/Catalog.Api --no-launch-profile -- --import artifacts/catalog-before-upgrade.json
   dotnet run --project src/Catalog.Api --no-launch-profile -- --export artifacts/catalog-after-upgrade.json
   ```

4. Compare the two files. Only start Catalog on the new database when they match. Preserve the old database as a backup. If duplicate product names prevent import, resolve those records explicitly; the import does not silently discard them.

This procedure is for the original database, which has no idempotency records. Future upgrades use migrations in place so saved request results are retained. A product-only export is not a complete backup of a migrated database.

## Subsequent schema changes

Create an EF migration and review it before deployment. Stop service instances, back up the database, then apply migrations once before restarting:

```powershell
dotnet run --project src/Catalog.Api --no-launch-profile -- --migrate
```

The checked-in initial migration and model snapshot establish the baseline. Never use `EnsureCreated` against the SQL database or run competing schema updates from multiple instances.

## Roll back to the monolith after service-side writes

1. Stop **both Web and PublicApi**, Catalog instances, and any other Catalog writers. Keep them stopped until verification finishes. Take full database backups.
2. Set `ConnectionStrings__CatalogDatabase` to the authoritative service database and `ConnectionStrings__LegacyCatalog` to the intended legacy database.
3. Reconcile and verify:

   ```powershell
   dotnet run --project src/Catalog.Api --no-launch-profile -- --restore-legacy artifacts/legacy-before-rollback.json
   dotnet run --project src/Catalog.Api --no-launch-profile -- --verify-legacy artifacts/verified-rollback.json
   ```

   The first command refuses an existing backup filename, backs up the legacy Catalog data, then applies inserts, updates and deletions in one transaction. It preserves IDs, verifies the result before commit, and advances existing HiLo sequences. A failed transaction leaves the legacy data unchanged. Baskets, orders and identity are not modified.

4. Only after both commands succeed, restart **both** monolith hosts with `UseCatalogApi=false`. Restarting discards cached HiLo IDs and server caches. Refresh browser-side admin caches as well.

Before any new writes, `--verify-legacy` alone can confirm that the databases still match. Verification is a point-in-time check: it is only a cutover gate while writers remain stopped. The flag itself cannot enforce this procedure.

## Retry contract

`POST /catalog/items` accepts an optional `Idempotency-Key` of 1–200 characters. Use a unique key per operation and reuse it only for retries of the same request. A matching retry returns the original 201 response; a changed payload returns 422. Invalid input and name conflicts are not recorded as completed operations.

SQL stores successful request results in `CatalogRequests` without automatic expiry. Back up this table with Catalog. Replaying a completed create returns its original result even if the product was subsequently edited or deleted. The monolith HTTP client supplies a key and retries once; cancellation stops retries. In-memory mode provides demo-only coordination and loses records on restart.

## Retire the migration path after deployment

Keep the legacy path until the service has operated successfully through the agreed observation period and rollback is no longer needed. Then remove the toggle and repository adapter, and retire only the old Catalog tables through a reviewed monolith migration. The original context/database also holds baskets and orders and must remain. This task rehearses cutover and recovery on temporary databases; it does not perform a live cutover or drop existing application tables.
