# AI Usage Disclosure

**This solution was implemented with Claude Code** (Anthropic's coding agent, model Claude Opus 5.5), working in this repository
from the written brief `CLAUDE_CODE_FULLSTACK_ASSIGNMENT.md`. This file is an honest, editable hand-off: it records what the AI did,
what was verified and how, and what a human still has to do. **Edit the "Human review" section yourself; do not let it claim more
than you actually did.**

## What was AI-generated

Effectively everything in the repository was written by Claude Code in one session (2026-10-21 → 2026-10-22):

- Backend: entities, EF Core configuration and migration, seeder and CLI commands, services, controllers, DTOs, validation,
  error handling, OpenAPI/Swagger configuration, `Program.cs`.
- Tests: all xUnit unit and integration tests, the test fixture and the race-injection interceptor; all Vitest specs.
- Frontend: all Angular components, store, services, styles.
- Scripts: `scripts/dev-*.sh`, `scripts/sql/*.sql`, `scripts/demo-api-flow.sh`, `scripts/perf/run_perf.py`.
- Docs: all Markdown files, including this one. Screenshots in `docs/screenshots/` were captured by the agent from the real app.

## What the human (candidate) did so far

- Provided the brief and asked for a full implementation.
- Declined one verification command partway through (a combined seed + SQL inspection step); the agent then split it into
  smaller commands, which were allowed and run.
- **No line-by-line human code review has happened yet.** See WORK_PLAN T12.

## What was verified, and how (all executed by the agent on the machine described in PERFORMANCE.md)

| Check | Command / method | Result |
|---|---|---|
| Backend build | `dotnet build` | Succeeded, 0 warnings (`TreatWarningsAsErrors` on the API) |
| Backend tests | `source scripts/dev-env.sh && dotnet test` (real SQL Server 2022 in Docker) | **67 passed, 0 failed, 0 skipped** |
| Angular build | `npm ci && npm run build` | Succeeded, 0 warnings |
| Angular tests | `npm test -- --watch=false` | **8 passed** |
| Seed | `scripts/dev-db-setup.sh`, re-run of `seed`, `reset-and-seed`, `scripts/sql/verify-seed.sql` | 100,000 + 133,740 rows; `seed` idempotent; identical distributions after reset; constraints trusted |
| Least-privilege runtime user | API run with `rm_app` (SELECT; UPDATE(Status, UpdatedAt); SELECT/INSERT on history) | All endpoints worked through the demo flow and the browser walkthrough |
| HTTP flow with real tokens | `scripts/demo-api-flow.sh` | 200 → 409 (stale) → 422 → one history row → bulk partial success → summary updated (docs/API_EXAMPLES.md) |
| Validation | ~30 curl calls + `ValidationTests` | Every invalid input returned 400 `ValidationFailed` with a field key |
| CORS | curl preflight from `localhost:4200` vs a foreign origin | Allowed origin gets `GET,PATCH` + `Content-Type,X-Actor-Id`; foreign origin gets no CORS headers |
| Swagger UI | Chrome (playwright-core driving the installed browser): `/swagger`, Try it out GET → PATCH → PATCH | 6 operations listed; 200, then 409 `ConcurrencyConflict` with the real token |
| UI in a real browser | Same tool, scripted walkthrough of http://localhost:4200 | 25-row page; typing "payment" sent **one** request; page 2 kept filters; status saved + history shown; 409 conflict → Refresh shows the other user's status, no overwrite on the server; bulk 2 succeeded / 1 InvalidTransition; an in-flight search was **aborted** (`net::ERR_ABORTED`) when a filter changed |
| Performance | `scripts/perf/run_perf.py` | docs/PERFORMANCE.md |
| Docker Compose full stack | `docker compose --profile app up -d --build`, curl checks, Chrome walkthroughs against the containers | seed + runtime-login steps exited 0; API/UI up as non-root; UI security headers + CSP with no browser errors; UI and Swagger walkthroughs passed; `run --rm db-seed reset-and-seed` worked |

### Mutation checks (do the tests catch the bugs they claim to?)

Each mutation was applied to a copy, the relevant tests were run, and the original file was restored byte-for-byte (`cmp`).

| Mutation | Tests failing |
|---|---|
| Backend: drop the version pre-check **and** `OriginalValue = expectedVersion` (save against the freshly loaded token) | 4 (`Two_clients…`, `Parallel…`, `Same_status…`, `Rejected_changes…`) |
| Backend: drop only the pre-check | 2 (`Same_status…` stale no-op, `Parallel…`), showing the DB condition alone still prevents the lost update |
| Backend: keep the pre-check, update without the version condition (`ExecuteUpdate`) | 2 (`A_write_committed_between_read_and_save…`, `Two_clients…`) |
| Angular: `switchMap` → `mergeMap` in the list pipeline | 1 (`cancels an in-flight request…`) |

### Defects found and fixed during verification

- OpenAPI listed query parameters in PascalCase; the XML-doc generator attached the `CancellationToken` description and the
  actor example to the request body → explicit `[FromQuery(Name)]`, header documented by an operation transformer.
- Validation error keys were PascalCase and messages showed `9.22E+18` → JSON-name metadata provider and contract-named messages.
- A known-vulnerable transitive package (`System.Security.Cryptography.Pkcs 9.0.11` via SqlClient) → pinned 10.0.13.
- UI: `<select [value]>` was applied before `@for` options existed, so the page-size selector showed 10 while 25 was active and
  the bulk selector showed "New" while "InProgress" would be sent → `[selected]` per option (found from a screenshot).
- UI: the dialog emitted `closed` twice; the conflict callout layout split a sentence.

## Known limitations and uncertainty

- Times in PERFORMANCE.md come from SQL Server under x86 emulation on Apple Silicon; absolute numbers on native x64 will differ.
  Logical reads and plan shapes should not.
- The demo identity header is not authentication (README § Security).
- The integration tests need a reachable SQL Server; without it they fail with a `BLOCKED:` message (by design: not skipped, not faked).
  Unit and smoke tests run without a database: `dotnet test --filter "Category!=Integration"`.
- Seed determinism was verified on .NET 10.0.5 / macOS arm64 only.
- Docker Compose on ARM was verified only on this machine's Rancher Desktop with Rosetta; other ARM setups (Docker Desktop
  without Rosetta, Linux ARM) were not tested.
- Swagger UI does not pre-fill example values from OpenAPI 3.1 `examples`; the operation descriptions contain sample bodies instead.

## Human review (to be completed by the candidate)

- [ ] Read every backend and frontend file and be able to answer the questions in MENTOR_WALKTHROUGH.md.
- [ ] Re-run README § Quick start and § Tests on your own machine; record results here.
- [ ] Decide whether you agree with each assumption in DECISIONS.md; change what you would do differently.
- [ ] Remove or reword anything in these docs you cannot personally stand behind.
