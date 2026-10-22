# Requirements Traceability

Every exam area mapped to code, tests and evidence. Paths are relative to the repository root; `Api/` = `backend/RequestManagement.Api/`,
`Tests/` = `backend/RequestManagement.Tests/`, `ui/` = `frontend/request-management-ui/src/app/`.
"Verified" means it was executed on 2026-10-22 and the result is recorded in the linked evidence. Nothing is marked done on the
basis of code alone.

| # | Exam area | Implementation | Automated tests | Evidence / docs | Status |
|---|---|---|---|---|---|
| 1 | **Data model + ≥100K repeatable seed** | `Api/Entities/*`, `Api/Data/Configurations/*` (lengths, nullability, check constraints, `rowversion`, indexes), migration `Api/Data/Migrations/20261022095317_InitialCreate.cs`, `Api/Data/DemoDataSeeder.cs`, `Api/Data/DemoCommands.cs` (`migrate`/`seed`/`reset-and-seed`) | `Tests/Unit/DemoDataSeederTests.cs` (deterministic, legal history, 200 orgs, ~15 % unassigned) | Seed output: 100,000 requests / 133,740 history rows; two runs gave identical distributions; `scripts/sql/verify-seed.sql` (constraints trusted, 0 incoherent rows). ADR-004 | Verified |
| 2 | **Server-side reads: filters, search, sorting, paging, DTOs, cancellation** | `Api/Controllers/RequestsController.cs` (`List`, `Get`), `Api/Services/RequestQueryService.cs` (`IQueryable` → `CountAsync` → whitelist `OrderBy` + `Id` → `Skip/Take` → `Select` DTO → `ToListAsync`, `AsNoTracking`, `CancellationToken` on every call), `Api/Contracts/Requests/RequestListQuery.cs`, `Api/Validation/StrictQueryModelBinders.cs` | `Tests/Integration/RequestListTests.cs` (filtered `totalCount`, page subset, tie breaker, asc/desc, case-insensitive `q`, inclusive/exclusive dates, page-size cap), `Tests/Integration/ValidationTests.cs` (25 cases: 23 × 400, plus 404 and 422) | EF SQL captured from the log (PERFORMANCE.md); API_EXAMPLES.md §0, §8 | Verified |
| 3 | **Status update + optimistic concurrency** | `Api/Services/RequestStatusService.cs` (`ChangeStatusAsync`: pre-check, transition rules, `OriginalValue = expectedVersion`, `DbUpdateConcurrencyException` → 409), `Api/Services/RequestStatusTransitions.cs`, `Api/Validation/RowVersionToken.cs`, `Api/Controllers/RequestsController.cs` (`UpdateStatus`: 200/400/403/404/409/422) | `Tests/Integration/ConcurrencyTests.cs` (same token twice → one 200 + one 409 + one audit row; competing commit injected between read and save → 409, winner kept, no audit row; 8 parallel requests → exactly one change; no-op vs stale no-op), `Tests/Unit/RequestStatusTransitionsTests.cs`, `Tests/Unit/RowVersionTokenTests.cs` | Mutation checks (AI_USAGE.md): saving with the loaded token or without the DB condition makes these tests fail. API_EXAMPLES.md §1–4. ADR-001 | Verified |
| 4 | **Audit history** | `Api/Entities/RequestStatusHistory.cs`, index `(RequestId, ChangedAt DESC, Id DESC)`, single `SaveChanges` for UPDATE + INSERT, `GetHistoryAsync`, `GET /api/requests/{id}/history`; DB-level immutability via `scripts/sql/create-runtime-user.sql` (runtime login has no UPDATE/DELETE on history) | `Tests/Integration/AuditHistoryTests.cs` (exactly one row, previous/new/actor, `ChangedAt == UpdatedAt`, UTC; rejected changes write nothing; newest-first ordering, `[]`, 404) | API_EXAMPLES.md §5; PERFORMANCE.md H1 (seek, 12 reads) | Verified |
| 5 | **Bulk (≤100, documented semantics)** | `RequestStatusService.BulkChangeStatusAsync` (sequential, one `DbContext` + transaction per item, same rules as single), `Api/Contracts/Requests/BulkStatusUpdate.cs` (1–100, duplicate IDs → 400), Swagger remarks explain the top-level `200` | `Tests/Integration/BulkStatusTests.cs` (Updated/NoChange/Conflict/InvalidTransition/NotFound in one batch, input order, successes committed + audited, failures untouched; empty/101/duplicate → 400 with nothing processed) | ADR-002; API_EXAMPLES.md §6; UI screenshot `docs/screenshots/05-bulk-partial-success.png`; 100-item batch measured at 0.38 s | Verified |
| 6 | **Angular** | `ui/requests/request-list.store.ts` (debounce 300 ms → `distinctUntilChanged` → `switchMap`, page reset, reload keeps filters, `toSignal`), `ui/requests/requests-api.service.ts`, `ui/requests/request.models.ts` (typed DTOs, no `any`), components: summary, filter bar, table (sortable, `aria-sort`), pagination, status dialog (409 Refresh UX, 422/400 messages), history, bulk panel; UTC formatting `ui/core/utc-date.pipe.ts` | `ui/requests/request-list.store.spec.ts` (debounce, distinct, cancellation of in-flight request, page reset, UTC date bounds, 400 error state), `ui/requests/status-dialog.spec.ts` (sends the read token + actor, 409 never auto-retries and Refresh shows fresh status/transitions, 422 message) | Real-Chrome walkthrough (AI_USAGE.md): one debounced request, aborted in-flight request (`net::ERR_ABORTED`), 409 → Refresh, bulk partial success; screenshots `docs/screenshots/01–05` | Verified |
| 7 | **Performance on 100K** | Indexes in `RequestConfiguration.cs` chosen per query pattern; `scripts/perf/run_perf.py` | — | `docs/PERFORMANCE.md` (STATISTICS IO/TIME, actual plans, before/after index, HTTP timings, bottlenecks + measured improvement); raw output `docs/perf/raw/` | Verified |
| 8 | **Cache** | `Api/Services/RequestSummaryService.cs` (`IMemoryCache`, 30 s absolute, generation-versioned key, invalidated only after commit) | `Tests/Integration/SummaryCacheTests.cs` (cached value really cached; new counts after a committed change; rejected changes don't invalidate) | ADR-003 (incl. multi-instance limitation); HTTP median 0.8 ms cached | Verified |
| 9 | **Tests (≥4, incl. integration + concurrency on real SQL Server)** | xUnit + `WebApplicationFactory` against SQL Server 2022 (`Tests/Infrastructure/SqlServerFixture.cs`, guarded `_Tests` database), Vitest for Angular | 67 backend tests, 8 Angular tests | README § Tests (exact commands and results) | Verified |
| 10 | **Work plan** | — | — | `docs/WORK_PLAN.md` (ordered tasks, dependencies graph, estimates, status, non-code tasks incl. human review left *Not Started*) | Done |
| 11 | **Code quality** | Thin controllers; 4 services; DTOs separate from entities; `ProblemDetails` everywhere (`Api/ErrorHandling/`); `TreatWarningsAsErrors`; nullable enabled; strict TS + strict templates | Whole suite | Build output: 0 warnings (backend), 0 warnings (Angular production build) | Verified |
| 12 | **Swagger / OpenAPI** | `Program.cs` (`AddOpenApi`, `MapOpenApi`, `UseSwaggerUI`, Development only), XML docs + `[ProducesResponseType]` for every status, `Api/Configuration/OpenApiConfiguration.cs` | `Tests/Integration/OpenApiSmokeTests.cs` (all 6 routes, 200/400/404/409/422 on PATCH, `X-Actor-Id` header, camelCase params, string enums, Swagger UI served) | Real-Chrome Try-it-out: GET → PATCH 200 → PATCH 409 (`docs/screenshots/06-swagger-try-it-out-409.png`) | Verified |
| 13 | **Validation, errors, logging, DI** | `[ApiController]` + DataAnnotations + `IValidatableObject`; strict binders; `ProblemResults` (code + traceId); `GlobalExceptionHandler` (no stack traces to clients); `ILogger<T>` write outcomes and conflicts (no tokens/payloads/connection strings); HTTP access log (method/path/status/duration only); `AddDbContextFactory` + scoped/singleton services; single CORS origin | `ValidationTests`, `OpenApiSmokeTests` (403 when demo identity disabled) | CORS preflight checked with curl (allowed origin vs foreign origin) | Verified |
| 14 | **Documentation / AI disclosure / submission** | — | — | `README.md`, `docs/DECISIONS.md`, `docs/API_EXAMPLES.md`, `docs/PERFORMANCE.md`, `docs/AI_USAGE.md`, `docs/MENTOR_WALKTHROUGH.md` | Done — human review pending (WORK_PLAN T12) |

## Definition of Done (brief §16)

Checked only where implemented **and** verified on 2026-10-22; the evidence is in the table above.

### API / DB
- [x] .NET API builds (0 warnings) and starts (`/health` 200).
- [x] `/swagger` is usable in Development (Try it out GET → PATCH 200 → PATCH 409, in Chrome).
- [x] `/openapi/v1.json` is valid OpenAPI 3.1 and documents status codes, schemas, query parameters and examples (smoke test + inspection).
- [x] SQL Server migrations apply and the seed creates 100,000 reproducible requests (+133,740 history rows).
- [x] Filtering, text search, sorting and paging run in SQL Server and return DTOs (EF SQL log, plans in PERFORMANCE.md).
- [x] API input validation is deliberate and returns coherent `ValidationProblemDetails` with `code`.
- [x] Cancellation tokens reach every async EF call (`CountAsync`, `ToListAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `SaveChangesAsync`, `ToDictionaryAsync`).
- [x] The client's expected row version is enforced in SQL; stale writes get 409; no lost update (incl. injected race and 8 parallel requests).
- [x] Successful changes update time/version and write one correct audit entry atomically.
- [x] History endpoint returns accurate, ordered data (`[]` / 404 cases).
- [x] Bulk ≤100 with partial success, one result per item, isolated commits.
- [x] Two DB-side aggregations (`GROUP BY Status`, `GROUP BY Priority`).
- [x] Summary cached 30 s and invalidated after committed changes only.
- [x] Meaningful logging and centralized error handling.

### Angular
- [x] Builds (production, 0 warnings) and displays requests and summary.
- [x] Server-side filtering, search, sorting and pagination wired to the API.
- [x] Search debounced; stale requests cancelled (unit test + `net::ERR_ABORTED` observed in Chrome).
- [x] Loading / error / empty states.
- [x] Single status change and 409 Refresh UX.
- [x] History and bulk outcomes visible.
- [x] No client-side filtering/materialisation of the 100K rows (one page per request, ≤100 rows).

### Quality and delivery
- [x] ≥4 valuable automated tests incl. real SQL Server integration/concurrency tests (67 backend + 8 Angular).
- [x] Tests run and reported truthfully (README § Tests; AI_USAGE.md).
- [x] ≥2 critical operations measured on 100K data (PERFORMANCE.md).
- [x] Requirements-to-code/tests traceability complete (this file).
- [x] Work plan with ordered tasks, dependencies, estimates, status and non-code tasks.
- [x] README with reproducible startup, Swagger usage, API examples and trade-offs.
- [x] Two major decisions with alternatives, limitations and improvements documented.
- [x] AI use disclosed truthfully.
- [ ] Code clear, comments useful, solution intentionally simple — *self-assessed by the AI only; needs the candidate's own review (WORK_PLAN T12).*
