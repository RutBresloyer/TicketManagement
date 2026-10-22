# Performance

All numbers on this page were **measured**, on 2026-10-22, by [`scripts/perf/run_perf.py`](../scripts/perf/run_perf.py) on the
seeded dataset. Full generated output: [`perf/latest-run.md`](perf/latest-run.md); raw `SET STATISTICS IO, TIME` output per
scenario: [`perf/raw/`](perf/raw/).

## Setup and method

| | |
|---|---|
| Machine | Apple M4 Max (14 cores, 36 GB), macOS 27 |
| Database | SQL Server 2022 Developer **16.0.4255.1 (RTM-CU25)**, official `mcr.microsoft.com/mssql/server:2022-latest` (linux/amd64) running **under x86-64 emulation (Rosetta)** in a Rancher Desktop VM (6 vCPU, 15.6 GiB) |
| Dataset | 100,000 `Requests`, 133,740 `RequestStatusHistory` (seed v1, random seed 20261001, anchor 2026-10-01Z) |
| Statements | Exactly the SQL EF Core 10 generates for the API (copied from the EF command log), run via `sp_executesql` with the same parameter types (`tinyint` status, `nvarchar(100)` assignee, …) |
| Metric | Warm buffer pool: 1 warm-up run, then **median of 5** runs with `SET STATISTICS IO, TIME ON`; logical reads (8 KB pages) are the robust metric. Plans from `SET STATISTICS XML ON` (actual rows). |
| HTTP | 20 sequential calls per endpoint after 1 warm-up, through the running API (Development, EF command logging on), `urllib` from the same machine |

**Caveat:** CPU/elapsed times are inflated by the emulation and are only comparable *within* this page. Logical reads and plan
shapes do not depend on the emulation.

## Where the work happens

Filtering, searching, sorting, counting and paging all happen **inside SQL Server**. `RequestQueryService.ListAsync` builds an
`IQueryable` (`AsNoTracking`), runs `COUNT(*)` with the same `WHERE`, then `ORDER BY … OFFSET … FETCH` with a `Select`
projection, so only the requested page (≤ 100 rows) is materialised in .NET. The Angular app never receives more than one page.
The summary is two `GROUP BY` queries returning 4 + 3 rows.

## Indexes and why

| Index | Serves | Evidence (logical reads) |
|---|---|---|
| `IX_Requests_CreatedAt_Id (CreatedAt DESC, Id DESC)` | default list order; date-range filters; also the narrowest index for an unfiltered `COUNT(*)` | L1 page 1: ordered scan of 25 rows + 25 key lookups = **79** |
| `IX_Requests_Status_CreatedAt_Id (Status, CreatedAt DESC, Id DESC)` | `status=` filter + default order; summary `GROUP BY Status` (narrow index scan, no sort) | S1: 774 |
| `IX_Requests_Priority_CreatedAt_Id (Priority, …)` | `priority=` filter; summary `GROUP BY Priority` | S2: 775 |
| `IX_Requests_AssignedTo_CreatedAt_Id (AssignedTo, …)` | "my requests" filter (2.1 % selectivity) | L3 COUNT **30** vs **2,525** without (see before/after) |
| `IX_Requests_OrganizationName_CreatedAt_Id (OrganizationName, …)` | exact organization filter (≈0.5 % selectivity) | L4 COUNT 12, page 80 |
| `IX_RequestStatusHistory_RequestId_ChangedAt_Id (RequestId, ChangedAt DESC, Id DESC)` | history endpoint, already in the required order; covers the FK | H1: 12 |

Each `Requests` index ends with `(CreatedAt DESC, Id DESC)` so that an equality filter on the leading column returns the first
page as an **ordered seek with no sort operator**, and stops after 25 rows.

## Results (SQL, warm, median of 5)

| Scenario | Plan (actual) | Logical reads | CPU ms | Elapsed ms |
|---|---|---:|---:|---:|
| L1 no filter — COUNT | Index Scan `IX_Requests_CreatedAt_Id` (100,000 rows) → Stream Aggregate | 764 | 10 | 11 |
| L1 no filter — page 1 | Top ← Index Scan `CreatedAt_Id` (25 rows, ordered) + 25 key lookups | 79 | 0 | 0 |
| L2 `status=New & priority=High` — COUNT | Hash Match of two index **seeks** (Priority 20,059 ∩ Status 24,836 → 4,950), 2 MB grant | 351 | 8 | 9 |
| L2 `status=New & priority=High` — page 1 | Seek `Priority_CreatedAt_Id` in order, residual `Status` via key lookup (109 rows read for 25) | 338 | 0 | 0 |
| L3 `assignedTo=agent-07` — COUNT | Seek `AssignedTo_CreatedAt_Id` (2,118 rows) | 30 | 0 | 0 |
| L3 `assignedTo=agent-07` — page 1 | Seek (25 rows, ordered) + 25 key lookups, no sort | 80 | 0 | 0 |
| L4 `organizationName` exact — COUNT / page 1 | Seek `OrganizationName_CreatedAt_Id` (467 rows) / + 25 lookups | 12 / 80 | 0 | 0 |
| **L5 `q=payment` — COUNT** | **Clustered Index Scan** of all 100,000 rows, `LIKE '%payment%'` on two columns (11,141 match) | **2,525** | **208** | **211** |
| L5 `q=payment` — page 1 | Ordered scan of `CreatedAt_Id` with lookups until 25 matches (239 rows read) | 736 | 0 | 1 |
| **L6 no filter — page 2000** (`OFFSET 49975`) | Ordered scan of 50,000 index rows **and 50,000 key lookups** | **153,511** | **51** | **58** |
| L6b *proposed* deferred join — page 2000 | Offset on the narrow index only (50,000 rows), then 25 lookups | 461 | 2 | 6 |
| L7 sort by title asc — page 1 | Clustered scan 100,000 → parallel **Sort** (Top-N), 35 MB grant | 2,654 | 105 | 21 |
| S1 summary `GROUP BY Status` | Index Scan `Status_CreatedAt_Id` → Stream Aggregate (ordered, no hash, no sort) | 774 | 10 | 13 |
| S2 summary `GROUP BY Priority` | Index Scan `Priority_CreatedAt_Id` → Stream Aggregate | 775 | 9 | 13 |
| H1 history of one request (3 entries) | Seek `RequestId_ChangedAt_Id` + 3 lookups, no sort | 12 | 0 | 0 |
| W1 status change batch (UPDATE + audit INSERT, rolled back) | Clustered seek/update + clustered insert | 15 + 6 | 0 | 0 |

## Before / after one index

`IX_Requests_AssignedTo_CreatedAt_Id` dropped **inside a transaction that is then rolled back** (same session, same data, same
warm cache — only the index differs):

| Scenario | With index | Without index |
|---|---|---|
| L3 `assignedTo=agent-07` — COUNT | Index Seek, **30** reads, 0 ms | Clustered Index Scan, **2,525** reads, 61 ms CPU |
| L3 `assignedTo=agent-07` — page 1 | Ordered seek + 25 lookups, **79–80** reads, no sort | Clustered scan + **Sort** (2.4 MB grant), **2,525** reads, 66 ms CPU |

## HTTP end-to-end (through the API)

| Endpoint | totalCount | median ms | p95 ms |
|---|---:|---:|---:|
| `GET /api/requests?page=1&pageSize=25` | 100,000 | 15.0 | 16.3 |
| `GET /api/requests?status=New&priority=High&page=1&pageSize=25` | 4,950 | 14.0 | 15.1 |
| `GET /api/requests?assignedTo=agent-07&page=1&pageSize=25` | 2,118 | 2.7 | 3.5 |
| `GET /api/requests?q=payment&page=1&pageSize=25` | 11,141 | 221.2 | 226.1 |
| `GET /api/requests?page=2000&pageSize=25` | 100,000 | 75.6 | 78.5 |
| `GET /api/requests/summary` (served from cache) | 100,000 | 0.8 | 1.7 |

A 100-item bulk status change (all `Updated`) took 0.38 s end-to-end in a single measurement (≈3.8 ms per item).

## Bottlenecks and practical improvements

1. **Substring search (`q`) — the main bottleneck.** `LIKE '%term%'` has a leading wildcard, so no B-tree index can seek it;
   the `COUNT` has to read every row (2,525 pages, ≈210 ms here). The indexes above do **not** help it, and we do not claim they
   do. The page query itself is cheap because it stops after 25 matches; the cost is the total count. Options, in order of effort:
   - SQL Server **Full-Text Search** (`CONTAINS`) on `Title`/`OrganizationName` — word/prefix semantics instead of arbitrary
     substrings; the most practical next step if search usage grows.
   - Skip the exact `totalCount` for searches (show "more than N" / cap the count).
   - A binary-collation shadow column (`UPPER(Title)` with `Latin1_General_100_BIN2`) makes each comparison cheaper but is still a scan.
2. **Deep OFFSET paging.** `OFFSET n` makes SQL Server read *and key-look-up* every skipped row: 153,511 reads for page 2000.
   Measured fix (L6b): a **deferred join** — page over the narrow covering index first, then fetch only the 25 rows — reduces it
   to 461 reads (≈10× faster here). Even better for "next page" navigation is **keyset pagination**
   (`WHERE (CreatedAt, Id) < (@lastCreatedAt, @lastId)`), which is O(page size) at any depth but changes the API contract
   (cursor instead of page number). Not implemented: the UI rarely goes past the first pages and plain OFFSET keeps the code simple.
3. **Sorting by a non-indexed column on the whole table** (L7: title) needs a full scan and a 35 MB sort grant. Acceptable at
   100K rows; with real usage data, add an index only for the sort orders people actually use.
4. **Combined filters** (L2) use index intersection for the count and a residual predicate for the page (109 rows read for 25).
   A composite `(Status, Priority, CreatedAt, Id)` would make it a pure seek, at the cost of one more index to maintain on every
   status change. Not added: the gain (≈338 → ≈80 reads) does not justify it at this scale.
5. **Write cost of the indexes.** A status change updates the clustered index and `IX_Requests_Status_CreatedAt_Id` only (other
   indexes do not contain `Status`/`UpdatedAt`); measured 15 + 6 logical reads for the whole UPDATE + INSERT batch.

## Reproduce

```bash
source scripts/dev-env.sh
dotnet run --project backend/RequestManagement.Api --launch-profile http -- reset-and-seed   # pristine dataset
dotnet run --project backend/RequestManagement.Api --launch-profile http &                    # for the HTTP part
python3 -I scripts/perf/run_perf.py docs/perf      # writes docs/perf/latest-run.md and docs/perf/raw/*.txt
```

The script only reads data; the single write scenario (W1) and the index drop run inside `BEGIN TRAN … ROLLBACK`.
To inspect plans interactively, open the statements from `scripts/perf/run_perf.py` in SSMS / Azure Data Studio with
"Include Actual Execution Plan". The SQL EF generates is visible in the API console in Development
(`Microsoft.EntityFrameworkCore.Database.Command` logged at Information, parameter values hidden).
