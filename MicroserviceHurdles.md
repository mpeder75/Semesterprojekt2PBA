# Microservice extraction: hurdles and lessons

This log records problems and decisions encountered while extracting services from the monolith. Catalog is the first example. For implementation details, see [CatalogExtraction.md](CatalogExtraction.md).

Keep each entry short: explain the problem, its impact, and the next step. Add new entries as problems appear; update existing entries when they are resolved. Proposed fixes are not completed work.

Status: **Resolved** = fixed and verified; **Deployment follow-up** = needs a live rollout decision; **Decision recorded** = approach established. Solutions below were implemented on 2026-09-30.

## 1. References across service boundaries

**Status:** Decision recorded · **Recorded:** 2026-09-30

- **Hurdle:** Baskets and orders need product IDs, but Catalog owns the products in a separate database.
- **Decision:** Store `CatalogItemId` as a required logical reference, without a cross-service database foreign key. A reference is nullable only when the business relationship is optional.
- **Lesson:** Foreign keys remain appropriate inside a service: Catalog products reference Catalog-owned brands and types. Orders retain product and price snapshots so later Catalog changes do not alter purchase history.

## 2. Deleted products can break baskets and checkout

**Status:** Resolved · **Recorded:** 2026-09-30

- **Problem:** Catalog permits product deletion. Basket display and checkout use `First(...)` to find every referenced product, which throws when a product is missing from the lookup response.
- **Impact:** A basket containing a deleted product can fail to display or complete checkout.
- **Solved:** Missing products now appear as unavailable and can be removed with quantity zero. Checkout rejects them before saving an order. Tests verify removal and unchanged historical orders.

## 3. Schema creation does not handle future database changes

**Status:** Resolved · **Recorded:** 2026-09-30

- **Problem:** Catalog uses `EnsureCreatedAsync()` for its initial database schema. It does not upgrade an existing schema when the model changes.
- **Impact:** Future deployments could run against an outdated database structure.
- **Solved:** Added EF migrations and an offline `--migrate` command. Old databases use verified export/import into a new database; SQL tests check that their original data is preserved.

## 4. Duplicate product writes are not fully protected

**Status:** Resolved · **Recorded:** 2026-09-30

- **Problem:** Creation checks for an existing name before inserting, but no unique database constraint enforces it. Concurrent requests can both pass. Updates do not apply the same name check.
- **Impact:** If names are meant to be unique, duplicates can still be saved.
- **Solved:** Kept the existing unique-name rule, added a Catalog database unique index and update validation. Concurrent requests to two service instances verify that only one duplicate name is accepted.

## 5. A timed-out write may already have succeeded

**Status:** Resolved · **Recorded:** 2026-09-30

- **Hurdle:** A service can save a product before its response is lost. The caller cannot infer failure from a timeout.
- **Solved:** Creation saves an idempotency key and response in the same SQL transaction as the product. The client retries once with the same key. Tests cover lost responses, concurrent retries and service restarts.

## 6. A feature flag is not a complete rollback strategy

**Status:** Recovery resolved; deployment follow-up for cleanup · **Recorded:** 2026-09-30

- **Hurdle:** The legacy Catalog implementation, tables, and feature flag remain during migration. After service-side writes, the old database is stale.
- **Impact:** Simply switching back can expose old data or lose access to recent changes. Uncoordinated switching can leave callers writing to different databases.
- **Solved:** Added offline backup/reconciliation and verification commands. SQL tests check changed data, ID allocation and all-or-nothing rollback. See [CatalogRecovery.md](CatalogRecovery.md).
- **Deployment follow-up:** Stop writers and coordinate both hosts during cutover. Remove the legacy path/tables only after stable service operation; no live cutover or deletion of existing application tables was performed.

## 7. Endpoint discovery loaded unrelated libraries

**Status:** Resolved · **Recorded:** 2026-09-30

- **Problem:** PublicApi scanned every loaded assembly for endpoints. When a SQL driver was already loaded, this could fail during startup.
- **Solved:** Registered and mapped the application's seven minimal endpoints explicitly. The combined service and API tests now pass regardless of that assembly being loaded.

## 8. Tests depended on Windows user-profile access

**Status:** Resolved · **Recorded:** 2026-09-30

- **Problem:** Test hosts tried to read the user's data-protection keys and write Windows event logs, causing permission failures.
- **Solved:** Test fixtures now use temporary in-memory protection keys and disable external log providers. All 92 automated tests pass; production settings are unchanged.

## Adding the next hurdle

Append a numbered entry above this section using:

```markdown
## N. Short, descriptive title

**Status:** Open / Resolved / Deployment follow-up / Decision recorded · **Recorded:** YYYY-MM-DD

- **Problem:** What we encountered.
- **Impact:** Why it matters.
- **Next step:** What we will do, or what decision we made.
```

When a fix is verified, change its status to **Resolved**, record the date, and replace the next step with the solution and a brief verification note.
