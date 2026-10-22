# Decisions

## What the exam requires vs. what we decided

The original assessment (six-page Hebrew PDF) asks for: a request-management system on .NET + Angular + a database with
≥100,000 generated requests; server-side filtering, search, sorting and paging; status changes with optimistic concurrency
(no lost update) and an audit history; bulk status updates of up to 100 requests with **documented** semantics; at least
two aggregations; a justified cache; ≥4 automated tests; a work plan; performance evidence; documentation and AI disclosure.

Everything below is an **implementation decision or assumption** made for this submission. None of it is claimed to be
specified by the exam:

| Decision / assumption | Where |
|---|---|
| Status transition matrix (`New → InProgress/Waiting`, `InProgress → Waiting/Completed`, `Waiting → InProgress/Completed`, `Completed` terminal) | `Services/RequestStatusTransitions.cs` |
| Same-status update = token-checked no-op (200, nothing written); stale token still 409 | `Services/RequestStatusService.cs` |
| Endpoint URLs and payload shapes (`/api/requests…`, `expectedVersion`, `rowVersion` as base64) | `Controllers/`, `Contracts/` |
| `X-Actor-Id` header as a **development-only** actor identity (not authentication) | `Configuration/DemoIdentityOptions.cs` |
| Bulk policy: **partial success**, ≤100 items, duplicate IDs rejected | ADR-002 |
| Page size 1–100 (default 25), `page` ≤ 1,000,000, `q` ≤ 100 chars | `Contracts/Requests/RequestListQuery.cs` |
| Exact filters and `q` are case-insensitive (column collation `Latin1_General_100_CI_AS`); `createdFrom` inclusive, `createdTo` exclusive; timestamps without offset are UTC | `Data/Configurations/RequestConfiguration.cs`, `Validation/StrictQueryModelBinders.cs` |
| Status/priority stored as `tinyint` (so `ORDER BY Priority` is Low < Medium < High), exposed as strings | `Entities/` |
| Summary `totalCount` = sum of the status groups (Status is NOT NULL) | `Services/RequestSummaryService.cs` |
| SQL Server as the database (exam leaves the choice open) | ADR-001 |

---

## ADR-001 — Optimistic concurrency with SQL Server `rowversion` + EF Core

**Context.** Two users can open the same request and both try to change its status. The exam requires no lost update and a
coherent `UpdatedAt`/version. Requests are edited rarely and briefly, so conflicts are uncommon.

**Options.**

1. *Pessimistic locking* (`SELECT … WITH (UPDLOCK)` held while the user decides, or an application "checked out" flag).
   Correct, but a lock would have to span a human think time across HTTP requests: abandoned locks, timeouts and lock
   management UI. Poor fit for a stateless REST API.
2. *Application-managed version* (an `int Version` column incremented in code, or comparing `UpdatedAt`). Works, but every
   writer must remember to increment it; timestamp comparison is fragile (precision, clock skew).
3. *Database-generated `rowversion` + EF Core optimistic concurrency.* SQL Server changes the value on **every** update of the
   row, by anyone (including scripts outside the app). EF adds `WHERE [RowVersion] = @original` and throws
   `DbUpdateConcurrencyException` when zero rows match.

**Decision.** Option 3. The client sends the `rowVersion` it read as `expectedVersion`; the service sets it as the property's
**original value** before `SaveChanges` (so the database checks the *client's* token, not the freshly loaded one), and maps a
concurrency exception to `409`. A pre-check against the loaded row gives an early 409 (and is what makes a stale "no-op" a
conflict), but the database condition is the guarantee. No process-local locks.

**Consequences.**
- No lost update even with several API instances, because the check is in the database (`ConcurrencyTests`).
- Clients must handle `409` (the Angular dialog explains the conflict and offers Refresh; it never retries by itself).
- The token is opaque base64; it must be passed back unchanged, and it is validated as exactly 8 bytes.
- Ties the implementation to a database with a rowversion-like feature (SQL Server here). Tests must use real SQL Server; EF
  InMemory/SQLite would not prove the behaviour.

## ADR-002 — Bulk status update: partial success with one transaction per item

**Context.** Up to 100 status changes in one call. Each item carries its own `expectedVersion`; in a real team some will be
stale or no longer allowed by the time the batch is sent. The exam lets us choose the semantics but requires them documented.

**Options.**

1. *All-or-nothing* (one transaction for the batch). Simple to reason about, but one stale row makes the whole batch fail, and
   the user must work out which one, refresh and resend 99 valid changes. It also holds locks on up to 100 rows for the whole batch.
2. *Partial success* (each item is its own unit of work). Valid items commit; failures are reported per item.

**Decision.** Option 2. Each item goes through exactly the same code path as the single endpoint
(`RequestStatusService.ChangeStatusAsync`), with a **new short-lived `DbContext` and its own `SaveChanges` transaction**, so a
failed item cannot leave tracked state behind or roll back other items. Items run **sequentially** (a `DbContext` is not
thread-safe, and 100 single-row updates do not need parallel connections). Response is `200` with `succeeded`, `failed` and one
`results` entry per input item, in input order; outcomes `Updated`, `NoChange`, `NotFound`, `ConcurrencyConflict`, `InvalidTransition`.

Contract errors are separated from business outcomes: an empty/oversized batch, a duplicated ID, or any item with an invalid
ID / unknown status / malformed token is a top-level `400` and **nothing** is processed. Duplicates are rejected because their
meaning ("which of the two wins?") would otherwise depend on processing order.

**Consequences.**
- Users keep the changes that were valid and get a precise list of what to refresh (`BulkStatusUpdateTests`).
- Clients must inspect `results`; a `200` does not mean every item changed (documented in Swagger and the UI).
- The batch is not atomic: if the process dies (or the client disconnects) mid-batch, earlier items stay committed. Each item
  is individually consistent and audited, and re-sending is safe because stale tokens are rejected.
- Throughput is bounded by sequential round trips (2 per item). Measured once locally: a 100-item batch (all `Updated`)
  took 0.38 s end-to-end, ≈3.8 ms per item, with SQL Server under x86 emulation. Acceptable at this size.

## ADR-003 — Cache only the summary, in process, with a versioned key

**Context.** `GET /api/requests/summary` aggregates the whole table (two `GROUP BY` scans, ≈775 logical reads each, ≈13 ms on
the 100K dataset) and is requested on every page load. Lists and single requests carry concurrency tokens and must be current.

**Options.** No cache; `IMemoryCache`; a distributed cache (Redis).

**Decision.** `IMemoryCache`, summary only, 30-second absolute expiration, invalidated **after** each committed status change
(single or bulk item; rejected changes do not invalidate). Invalidation increments a generation counter that is part of the
cache key, so a summary computation that started before a commit and finishes after it is stored under the old key and can
never be served again (a plain `Remove` would let that in-flight fill repopulate stale data).

**Consequences.**
- Repeat summary calls are served in < 1 ms (HTTP median 0.8 ms, see PERFORMANCE.md); a change through this API is visible on
  the next call (`SummaryCacheTests`).
- **Process-local**: with N API instances, an update on instance A does not invalidate instance B, which can serve counts up
  to 30 s old. Changes made outside the API (SQL scripts, `reset-and-seed`) are also only seen after expiry.
- A real multi-instance deployment would need Redis/`IDistributedCache` (or `HybridCache`) plus a cross-instance invalidation
  signal (pub/sub), and would have to handle the same in-flight-fill race across processes. Redis is intentionally not part of
  this solution: it adds infrastructure for a counter that is allowed to be 30 s old.

## ADR-004 — Seeder with SqlBulkCopy, fixed seed and fixed time anchor

**Context.** ≥100,000 requests must be generated repeatably, and benchmarks must be comparable across runs.

**Decision.** Pure in-memory generation from `Random(20261001)` and a fixed anchor `2026-10-01T00:00:00Z` (no "now"), with
explicit IDs 1..N, streamed with `SqlBulkCopy` (`KeepIdentity | CheckConstraints | TableLock`) in one transaction.
`CheckConstraints` keeps the FK and CHECK constraints *trusted*. A `DemoSeedInfo` row marks the database as a demo dataset:
`seed` is a no-op when it exists and refuses to touch a non-empty unmarked database; `reset-and-seed` only runs in Development
and only on a marked (or empty) database. The API never seeds or migrates on startup.

**Consequences.** 100,000 requests + 133,740 history rows in 3–8 s; two independent runs produced identical distributions.
Generation is deterministic for a given .NET runtime (seeded `System.Random` uses its legacy, stable algorithm).

## ADR-005 — No real authentication; development-only `X-Actor-Id`

**Context.** The audit needs a `ChangedBy`. The exam does not ask for a login system.

**Decision.** Mutations require `X-Actor-Id` (3–64 chars, `[A-Za-z0-9._-]`, no `@` so e-mail addresses stay out of the audit).
It is enabled only where `DemoIdentity:Enabled = true` (appsettings.Development.json); elsewhere mutations return `403
DemoIdentityDisabled`. **It is not authentication** — any caller can claim any actor.

**Consequences.** Self-contained demo and tests. A real deployment must add authentication (e.g. JWT bearer / OpenID Connect)
and resolve `ChangedBy` from the validated principal's claims, removing the header.
