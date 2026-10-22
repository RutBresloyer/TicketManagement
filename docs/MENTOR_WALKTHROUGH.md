# Mentor Walkthrough

A guide for presenting and reviewing this solution, in eight topics. Each one gives the files to open, something to run, and
questions the candidate should be able to answer without notes. Setup: README § Quick start (API on :5080, UI on :4200).

---

## 1. Why the architecture is intentionally simple

**Look at:** `backend/RequestManagement.Api/` — 2 controllers, 4 services, 1 `DbContext`, DTOs in `Contracts/`. No repositories,
no MediatR/CQRS, no domain events, no interfaces around single implementations. One test project.

**Demo:** follow one call end to end: `RequestsController.UpdateStatus` → `RequestStatusService.ChangeStatusAsync` →
`AppDbContext` → SQL in the API console log.

**Questions**
- EF Core already is a unit of work and a repository. What would an extra repository layer add here, and what would it cost?
- Which part of this code would you split out first if the team doubled, and why that part?
- Why is `RequestSummaryService` a singleton while the other services are scoped? What would break if it were scoped?

## 2. How LINQ becomes efficient SQL, and where pagination executes

**Look at:** `Services/RequestQueryService.cs` (`ApplyFilters`, `ApplyOrdering`, the `Select` projection);
`docs/PERFORMANCE.md` § Where the work happens.

**Demo:** call `GET /api/requests?status=New&q=payment&page=3` and read the two SQL statements in the API console:
`SELECT COUNT(*) … WHERE …` and `SELECT … ORDER BY [CreatedAt] DESC, [Id] DESC OFFSET @p ROWS FETCH NEXT @p5 ROWS ONLY`.

**Questions**
- What happens if someone adds `.ToList()` right after `ApplyFilters`? How would you notice in review and in production?
- Why is `Id` always the last `ORDER BY` key? Show a case where pages would overlap without it.
- `RowVersionToken.Encode` and `AllowedNextStatuses` run in .NET inside the `Select` — why is that fine here, and when would
  client evaluation inside a `Select` be a problem?

## 3. Indexes for specific filters, and why substring search is different

**Look at:** `Data/Configurations/RequestConfiguration.cs` (index definitions and the comment on their shape);
`docs/PERFORMANCE.md` (plans, before/after for the assignee index).

**Demo:** `python3 -I scripts/perf/run_perf.py` then compare L3 (assignee seek, 30 reads) with L5 (`q=payment`, clustered scan,
2,525 reads), and the "without index" plan for L3 (scan + sort).

**Questions**
- Why does every index end with `(CreatedAt DESC, Id DESC)`? What operator disappears from the plan because of it?
- Why can't `IX_Requests_*` help `LIKE '%payment%'`, but could help `LIKE 'payment%'`?
- Each index costs something on writes. Which indexes does a status change actually have to update?

## 4. How optimistic concurrency prevents lost updates — and how it can still go wrong

**Look at:** `Services/RequestStatusService.cs` (`OriginalValue = expectedVersion`, the pre-check, the
`DbUpdateConcurrencyException` catch); `Integration/ConcurrencyTests.cs`; `Infrastructure/CompetingWriteInterceptor.cs`.

**Demo:** `scripts/demo-api-flow.sh` steps 1–3 (read → 200 → same token → 409). Then run only
`dotnet test --filter FullyQualifiedName~ConcurrencyTests`.
These mutations were tried during development (each test run, then reverted; see AI_USAGE.md):

| Mutation | Result |
|---|---|
| Remove the pre-check **and** the `OriginalValue` line (save with the freshly loaded token) | 4 tests fail: lost update |
| Remove only the pre-check | 2 fail: the DB check still prevents the lost update, but a stale "no-op" is reported as success |
| Keep the pre-check, update without the version condition (`ExecuteUpdate`) | 2 fail: the race test detects the overwrite |

**Questions**
- The pre-check already compares tokens. Why is it not sufficient on its own? Describe the exact interleaving.
- Why must the *client's* token be the original value, not the one EF just loaded? What bug does that line prevent?
- Why is setting the same status with a stale token a `409` and not a `200`?

## 5. Why change + audit are one atomic unit

**Look at:** `ChangeStatusAsync` — one `SaveChanges` for the `UPDATE` and the history `INSERT`; the EF command log shows both
statements in **one batch**, INSERT first; `Integration/AuditHistoryTests.cs`; `scripts/sql/create-runtime-user.sql`.

**Demo:** in the API log of a status change, find `INSERT INTO [RequestStatusHistory] … ; UPDATE [Requests] … WHERE [Id] = @p7
AND [RowVersion] = @p8;`. Then explain why the race test (`A_write_committed_between_read_and_save…`) finds **zero** audit rows.

**Questions**
- The INSERT runs before the UPDATE in the batch. What guarantees that a conflicting UPDATE leaves no audit row?
- Why is `ChangedAt` sampled once and used for both `UpdatedAt` and the audit row?
- The runtime login `rm_app` has `INSERT` but no `UPDATE`/`DELETE` on `RequestStatusHistory`. What does that protect against
  that application code alone cannot?

## 6. Bulk partial success and its trade-off

**Look at:** `RequestStatusService.BulkChangeStatusAsync`; `Contracts/Requests/BulkStatusUpdate.cs` (top-level validation);
`docs/DECISIONS.md` ADR-002; `Integration/BulkStatusTests.cs`.

**Demo:** `scripts/demo-api-flow.sh` step 6 (Updated + ConcurrencyConflict + NotFound in one call). In the UI, select rows of
mixed status, set "Completed", and read the per-item result table.

**Questions**
- Why one `DbContext` per item instead of one for the batch? What could a failed item's tracked state do to the next one?
- Why sequential and not `Task.WhenAll`?
- Which errors are a top-level `400` (nothing processed) and which are per-item outcomes? Why is a duplicate ID top-level?

## 7. What `switchMap`, cancellation and debounce actually solve

**Look at:** `frontend/request-management-ui/src/app/requests/request-list.store.ts` (the pipeline and its diagram comment);
`request-list.store.spec.ts`.

**Demo:** in the browser dev tools Network tab, type "payment" quickly: one request. Change a filter while a slow `q` search is
running: the first request shows as cancelled. Run `npm test`.

**Questions**
- What exactly does `switchMap` unsubscribe from, and how does that become an aborted HTTP request?
- What would the user see with `mergeMap` instead? (The cancellation test fails — try it.)
- Why does a filter change reset to page 1 but a page change keep the filters? Point to the operator that does it.

## 8. How caching, invalidation and the integration tests validate correctness

**Look at:** `Services/RequestSummaryService.cs` (generation counter); `Integration/SummaryCacheTests.cs`;
`Infrastructure/SqlServerFixture.cs` (real SQL Server, guarded `_Tests` database).

**Demo:** `dotnet test --filter FullyQualifiedName~SummaryCacheTests`. Then call the summary twice and compare `generatedAt`;
change a status; call again.

**Questions**
- Why invalidate *after* `SaveChanges`, and what goes wrong with a plain `cache.Remove` when a summary query is in flight?
- With two API instances behind a load balancer, what does a user see after changing a status? What would you build?
- Why do the integration tests use real SQL Server instead of EF InMemory, and how do they stay isolated and rerunnable?
