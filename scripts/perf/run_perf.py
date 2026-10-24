#!/usr/bin/env python3
"""
Performance measurements for docs/PERFORMANCE.md on the seeded 100K demo dataset.

* Replays the SQL that EF Core generates for the API (copied from the EF command log) through sp_executesql with the
  same parameter types, inside the local SQL Server container.
* For each scenario: 1 warm-up run, then 5 runs with SET STATISTICS IO, TIME ON (median CPU/elapsed, logical reads),
  then 1 run with SET STATISTICS XML ON to summarise the ACTUAL execution plan (operators, indexes, rows, memory grant).
* Before/after: the assignee scenario is re-measured inside a transaction that drops its index and is then ROLLED BACK,
  so the database is left unchanged.
* HTTP: median/p95 of 20 calls per endpoint against the running API (includes JSON + Kestrel + in-process overhead).

Read-only for the data (the one write scenario runs inside BEGIN TRAN ... ROLLBACK).
Usage: python3 scripts/perf/run_perf.py [output-dir]     (default: docs/perf; Windows: py -3 scripts/perf/run_perf.py)
Requires: the rm-sqlserver container (docker compose) and, for the HTTP part, the API on http://localhost:5080.
"""
import json
import os
import re
import statistics
import subprocess
import sys
import time
import urllib.request
import xml.etree.ElementTree as ET

CONTAINER = "rm-sqlserver"
DATABASE = "RequestManagementDemo"
API = "http://localhost:5080"
RUNS = 5
NS = {"p": "http://schemas.microsoft.com/sqlserver/2004/07/showplan"}

COLUMNS = ("[r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status], [r].[Priority], [r].[AssignedTo], "
           "[r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]")
DEFAULT_ORDER = "[r].[CreatedAt] DESC, [r].[Id] DESC"


def page_sql(where="", order=DEFAULT_ORDER):
    return f"SELECT {COLUMNS} FROM [Requests] AS [r]{where} ORDER BY {order} OFFSET @p ROWS FETCH NEXT @n ROWS ONLY"


def count_sql(where=""):
    return f"SELECT COUNT(*) FROM [Requests] AS [r]{where}"


def call(sql, decl="", values=""):
    """sp_executesql call, as EF sends it."""
    escaped = sql.replace("'", "''")
    if not decl:
        return f"EXEC sp_executesql N'{escaped}';"
    return f"EXEC sp_executesql N'{escaped}', N'{decl}', {values};"


W_STATUS_PRIORITY = " WHERE [r].[Status] = @status AND [r].[Priority] = @priority"
W_ASSIGNEE = " WHERE [r].[AssignedTo] = @assignedTo"
W_ORG = " WHERE [r].[OrganizationName] = @org"
W_SEARCH = (" WHERE [r].[Title] LIKE @term_contains ESCAPE N'\\' "
            "OR [r].[OrganizationName] LIKE @term_contains0 ESCAPE N'\\'")
PAGE_DECL = "@p int, @n int"

SCENARIOS = [
    ("L1 list, no filter: COUNT", call(count_sql())),
    ("L1 list, no filter: page 1", call(page_sql(), PAGE_DECL, "@p = 0, @n = 25")),
    ("L2 status=New & priority=High: COUNT",
     call(count_sql(W_STATUS_PRIORITY), "@status tinyint, @priority tinyint", "@status = 1, @priority = 3")),
    ("L2 status=New & priority=High: page 1",
     call(page_sql(W_STATUS_PRIORITY), "@status tinyint, @priority tinyint, " + PAGE_DECL,
          "@status = 1, @priority = 3, @p = 0, @n = 25")),
    ("L3 assignedTo=agent-07: COUNT",
     call(count_sql(W_ASSIGNEE), "@assignedTo nvarchar(100)", "@assignedTo = N'agent-07'")),
    ("L3 assignedTo=agent-07: page 1",
     call(page_sql(W_ASSIGNEE), "@assignedTo nvarchar(100), " + PAGE_DECL, "@assignedTo = N'agent-07', @p = 0, @n = 25")),
    ("L4 organizationName exact: COUNT",
     call(count_sql(W_ORG), "@org nvarchar(150)", "@org = N'Blue Harbor Logistics'")),
    ("L4 organizationName exact: page 1",
     call(page_sql(W_ORG), "@org nvarchar(150), " + PAGE_DECL, "@org = N'Blue Harbor Logistics', @p = 0, @n = 25")),
    ("L5 q=payment: COUNT",
     call(count_sql(W_SEARCH), "@term_contains nvarchar(200), @term_contains0 nvarchar(150)",
          "@term_contains = N'%payment%', @term_contains0 = N'%payment%'")),
    ("L5 q=payment: page 1",
     call(page_sql(W_SEARCH), "@term_contains nvarchar(200), @term_contains0 nvarchar(150), " + PAGE_DECL,
          "@term_contains = N'%payment%', @term_contains0 = N'%payment%', @p = 0, @n = 25")),
    ("L6 no filter: page 2000 (OFFSET 49975)", call(page_sql(), PAGE_DECL, "@p = 49975, @n = 25")),
    # Proposed improvement for L6 (not used by the API): page the narrow index first, then look up only 25 rows.
    ("L6b PROPOSED deferred join: page 2000",
     call(f"SELECT {COLUMNS} FROM [Requests] AS [r] INNER JOIN (SELECT [Id], [CreatedAt] FROM [Requests] "
          f"ORDER BY [CreatedAt] DESC, [Id] DESC OFFSET @p ROWS FETCH NEXT @n ROWS ONLY) AS [k] ON [k].[Id] = [r].[Id] "
          f"ORDER BY [k].[CreatedAt] DESC, [k].[Id] DESC", PAGE_DECL, "@p = 49975, @n = 25")),
    ("L7 sort by title asc: page 1", call(page_sql(order="[r].[Title], [r].[Id]"), PAGE_DECL, "@p = 0, @n = 25")),
    ("S1 summary GROUP BY Status",
     call("SELECT [r].[Status], COUNT(*) AS [Count] FROM [Requests] AS [r] GROUP BY [r].[Status]")),
    ("S2 summary GROUP BY Priority",
     call("SELECT [r].[Priority], COUNT(*) AS [Count] FROM [Requests] AS [r] GROUP BY [r].[Priority]")),
    ("H1 history of request 5 (3 entries)",
     call("SELECT [r].[Id], [r].[PreviousStatus], [r].[NewStatus], [r].[ChangedAt], [r].[ChangedBy] "
          "FROM [RequestStatusHistory] AS [r] WHERE [r].[RequestId] = @id ORDER BY [r].[ChangedAt] DESC, [r].[Id] DESC",
          "@id bigint", "@id = 5")),
]

# The status UPDATE + audit INSERT batch EF sends, measured inside a rolled-back transaction.
WRITE_SCENARIO = ("W1 status change batch (rolled back)", """
BEGIN TRAN;
DECLARE @v binary(8) = (SELECT RowVersion FROM dbo.Requests WHERE Id = 1);
DECLARE @now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);
EXEC sp_executesql N'SET NOCOUNT ON;
INSERT INTO [RequestStatusHistory] ([ChangedAt], [ChangedBy], [NewStatus], [PreviousStatus], [RequestId])
OUTPUT INSERTED.[Id] VALUES (@p0, @p1, @p2, @p3, @p4);
UPDATE [Requests] SET [Status] = @p5, [UpdatedAt] = @p6 OUTPUT INSERTED.[RowVersion] WHERE [Id] = @p7 AND [RowVersion] = @p8;',
 N'@p0 datetimeoffset, @p1 nvarchar(100), @p2 tinyint, @p3 tinyint, @p4 bigint, @p5 tinyint, @p6 datetimeoffset, @p7 bigint, @p8 binary(8)',
 @p0 = @now, @p1 = N'perf-probe', @p2 = 2, @p3 = 1, @p4 = 1, @p5 = 2, @p6 = @now, @p7 = 1, @p8 = @v;
ROLLBACK;
""")


def sqlcmd(sql):
    command = ["docker", "exec", "-i", CONTAINER, "bash", "-c",
               'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd '
               f"-S localhost -U sa -C -b -d {DATABASE} -y 0 -i /dev/stdin"]
    return subprocess.run(command, input=sql, capture_output=True, text=True, encoding="utf-8", check=True).stdout


def measure(sql):
    sqlcmd("SET NOCOUNT ON;\n" + sql)  # warm-up
    runs = []
    raw = []
    for _ in range(RUNS):
        out = sqlcmd("SET NOCOUNT ON; SET STATISTICS IO, TIME ON;\n" + sql + "\nSET STATISTICS IO, TIME OFF;")
        raw.append(out)
        times = [(int(c), int(e)) for c, e in re.findall(r"CPU time = (\d+) ms,\s+elapsed time = (\d+) ms", out)]
        reads = {}
        for table, scans, logical in re.findall(r"Table '([^']+)'\. Scan count (\d+), logical reads (\d+)", out):
            reads[table] = reads.get(table, 0) + int(logical)
        runs.append({"cpu": max(t[0] for t in times), "elapsed": max(t[1] for t in times), "reads": reads})
    return {
        "cpu_ms": statistics.median(r["cpu"] for r in runs),
        "elapsed_ms": statistics.median(r["elapsed"] for r in runs),
        "logical_reads": runs[-1]["reads"],
    }, raw[-1]


def plan(sql):
    out = sqlcmd("SET NOCOUNT ON; SET STATISTICS XML ON;\n" + sql + "\nSET STATISTICS XML OFF;")
    plans = re.findall(r"<ShowPlanXML.*?</ShowPlanXML>", out, re.S)
    lines = []
    for xml in plans:
        root = ET.fromstring(xml)
        for stmt in root.iter(f"{{{NS['p']}}}StmtSimple"):
            query_plan = stmt.find("p:QueryPlan", NS)
            if query_plan is None:
                continue
            grant = query_plan.find("p:MemoryGrantInfo", NS)
            granted = grant.get("GrantedMemory") if grant is not None else "0"
            lines.append(f"memory grant: {granted} KB")
            top = query_plan.find("p:RelOp", NS)
            walk(top, 0, lines)
    return lines


def walk(relop, depth, lines):
    if relop is None:
        return
    actual = sum(int(t.get("ActualRows", 0)) for t in relop.iter(f"{{{NS['p']}}}RunTimeCountersPerThread")
                 if t in list(relop.find("p:RunTimeInformation", NS) or []))
    index = ""
    for child in relop:
        obj = child.find("p:Object", NS)
        if obj is not None and child.tag.split("}")[1] in ("IndexScan", "Update", "SimpleUpdate"):
            index = " on " + (obj.get("Index") or obj.get("Table") or "").strip("[]")
            if child.find("p:SeekPredicates", NS) is not None and relop.get("PhysicalOp") in ("Index Seek", "Clustered Index Seek"):
                index += " (seek)"
            break
    lines.append(f"{'  ' * depth}{relop.get('PhysicalOp')}{index} — est {float(relop.get('EstimateRows')):.0f}, actual {actual}")
    for child in relop.iter(f"{{{NS['p']}}}RelOp"):
        if child is relop:
            continue
        if parent_relop(relop, child):
            walk(child, depth + 1, lines)


def parent_relop(parent, candidate):
    """True if candidate is a direct RelOp descendant of parent (no RelOp in between)."""
    def search(node):
        for child in node:
            if child is candidate:
                return True
            if child.tag.endswith("RelOp"):
                continue
            if search(child):
                return True
        return False
    return search(parent)


def http_timings():
    endpoints = [
        "/api/requests?page=1&pageSize=25",
        "/api/requests?status=New&priority=High&page=1&pageSize=25",
        "/api/requests?assignedTo=agent-07&page=1&pageSize=25",
        "/api/requests?q=payment&page=1&pageSize=25",
        "/api/requests?page=2000&pageSize=25",
        "/api/requests/summary",
    ]
    results = []
    for path in endpoints:
        urllib.request.urlopen(API + path).read()  # warm-up
        samples = []
        for _ in range(20):
            start = time.perf_counter()
            body = urllib.request.urlopen(API + path).read()
            samples.append((time.perf_counter() - start) * 1000)
        samples.sort()
        total = json.loads(body).get("totalCount")
        results.append((path, statistics.median(samples), samples[int(len(samples) * 0.95) - 1], total))
    return results


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "docs/perf"
    raw_dir = os.path.join(out_dir, "raw")
    os.makedirs(raw_dir, exist_ok=True)
    report = []
    env = sqlcmd("SET NOCOUNT ON; SELECT @@VERSION; SELECT COUNT(*) FROM dbo.Requests; "
                 "SELECT COUNT(*) FROM dbo.RequestStatusHistory;")
    report.append("## Environment (captured by the script)\n\n```text\n" + re.sub(r"\n\s*\n", "\n", env).strip() + "\n```\n")

    report.append("## SQL measurements (warm cache, median of 5)\n")
    report.append("| Scenario | Logical reads | CPU ms | Elapsed ms |\n|---|---|---:|---:|")
    plans = []
    for name, sql in SCENARIOS + [WRITE_SCENARIO]:
        stats, raw = measure(sql)
        slug = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")
        with open(os.path.join(raw_dir, slug + ".txt"), "w", encoding="utf-8") as f:
            f.write(raw)
        reads = ", ".join(f"{t} {n}" for t, n in stats["logical_reads"].items())
        report.append(f"| {name} | {reads} | {stats['cpu_ms']:.0f} | {stats['elapsed_ms']:.0f} |")
        plans.append((name, plan(sql)))
        print(f"measured: {name}", file=sys.stderr)

    report.append("\n## Actual plan shapes\n")
    for name, lines in plans:
        report.append(f"**{name}**\n\n```text\n" + "\n".join(lines) + "\n```\n")

    report.append("## Before/after: IX_Requests_AssignedTo_CreatedAt_Id (dropped inside a rolled-back transaction)\n")
    report.append("| Scenario | Index | Logical reads | CPU ms | Elapsed ms |\n|---|---|---|---:|---:|")
    for name, sql in [s for s in SCENARIOS if s[0].startswith("L3")]:
        with_index, _ = measure(sql)
        without_sql = ("BEGIN TRAN; DROP INDEX IX_Requests_AssignedTo_CreatedAt_Id ON dbo.Requests;\n"
                       + sql + "\nROLLBACK;")
        without_index, _ = measure(without_sql)
        no_index_plan = plan(without_sql)
        for label, s in (("with", with_index), ("without", without_index)):
            reads = ", ".join(f"{t} {n}" for t, n in s["logical_reads"].items())
            report.append(f"| {name} | {label} | {reads} | {s['cpu_ms']:.0f} | {s['elapsed_ms']:.0f} |")
        plans.append((name + " — WITHOUT index", no_index_plan))
    report.append("\n" + "\n".join(f"**{n}**\n\n```text\n" + "\n".join(l) + "\n```\n" for n, l in plans[-2:]))

    try:
        timings = http_timings()
        report.append("## HTTP end-to-end through the API (20 calls each, after 1 warm-up)\n")
        report.append("| Endpoint | totalCount | median ms | p95 ms |\n|---|---:|---:|---:|")
        for path, median, p95, total in timings:
            report.append(f"| `{path}` | {total if total is not None else '—'} | {median:.1f} | {p95:.1f} |")
    except OSError as error:
        report.append(f"## HTTP end-to-end\n\nNOT MEASURED — API not reachable: {error}\n")

    with open(os.path.join(out_dir, "latest-run.md"), "w", encoding="utf-8") as f:
        f.write("# Performance run output\n\nGenerated by `scripts/perf/run_perf.py`; raw STATISTICS IO/TIME output in `raw/`.\n\n")
        f.write("\n".join(report) + "\n")
    print("\n".join(report))


if __name__ == "__main__":
    main()
