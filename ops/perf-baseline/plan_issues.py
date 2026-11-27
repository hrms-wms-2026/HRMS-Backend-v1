"""Task 18: list the plan problems of the top Query Store queries.

  python ops/perf-baseline/plan_issues.py --server localhost,1433 --database CrownusDb_perf --top 30 --out top.json

Reads the top queries (query-store-top.sql), fetches each query's plan from Query Store with sqlcmd, and lists:
scans (with the table's row count), key lookups, sorts, and the missing-index hints SQL Server wrote into the plan.
Query Store keeps estimated plans, so row counts are the optimizer's estimates; the table size is exact.

Needs sqlcmd on PATH. Signs in with SQLCMDUSER / SQLCMDPASSWORD when they are set, otherwise Windows authentication.
"""

import argparse
import json
import os
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET

NS = {"p": "http://schemas.microsoft.com/sqlserver/2004/07/showplan"}
SCANS = {"Clustered Index Scan", "Index Scan", "Table Scan"}
HERE = pathlib.Path(__file__).resolve().parent


def sqlcmd(server, database, args):
    auth = ["-U", os.environ["SQLCMDUSER"]] if os.environ.get("SQLCMDUSER") else ["-E"]
    cmd = ["sqlcmd", "-S", server, "-d", database, "-C", "-b", *auth, *args]
    run = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if run.returncode != 0:
        sys.exit(f"sqlcmd failed: {run.stderr or run.stdout}")
    return run.stdout


def top_queries(server, database, top):
    out = sqlcmd(server, database, ["-i", str(HERE / "query-store-top.sql"), "-v", f"TOP={top}",
                                    "-W", "-s", "\x1f", "-h", "-1"])
    rows = []
    for line in out.splitlines():
        parts = line.split("\x1f")
        if len(parts) < 10 or not parts[0].strip().isdigit():
            continue
        rows.append({
            "by_duration": int(parts[0]), "by_reads": int(parts[1]), "query_id": int(parts[2]),
            "plan_id": int(parts[3]), "executions": int(parts[4]), "avg_ms": float(parts[5]),
            "total_ms": float(parts[6]), "avg_reads": int(parts[7]), "avg_rows": int(parts[8]),
            "query_text": "\x1f".join(parts[9:]).strip(),
        })
    return rows


def plan_xml(server, database, plan_id):
    out = sqlcmd(server, database, ["-y", "0", "-Q",
                                    f"SET NOCOUNT ON; SELECT query_plan FROM sys.query_store_plan WHERE plan_id = {plan_id}"])
    return out.strip()


def table_name(obj):
    table = (obj.get("Table") or "").strip("[]")
    index = (obj.get("Index") or "").strip("[]")
    return table, index


def issues(xml_text):
    found = {"scans": [], "key_lookups": [], "sorts": 0, "missing_indexes": []}
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError:
        found["error"] = "plan not readable"
        return found
    for op in root.iter(f"{{{NS['p']}}}RelOp"):
        physical = op.get("PhysicalOp")
        obj = op.find(".//p:Object", NS)
        if physical in SCANS and obj is not None:
            table, index = table_name(obj)
            found["scans"].append({
                "table": table, "index": index, "op": physical,
                "table_rows": float(op.get("TableCardinality") or 0),
                "est_rows_read": float(op.get("EstimatedRowsRead") or op.get("EstimateRows") or 0),
                "est_executions": float(op.get("EstimateRebinds") or 0) + float(op.get("EstimateRewinds") or 0) + 1,
            })
        elif physical == "Key Lookup" and obj is not None:
            table, index = table_name(obj)
            found["key_lookups"].append({"table": table, "index": index})
        elif physical == "Sort":
            found["sorts"] += 1
    for group in root.iter(f"{{{NS['p']}}}MissingIndexGroup"):
        for mi in group.findall("p:MissingIndex", NS):
            cols = {"EQUALITY": [], "INEQUALITY": [], "INCLUDE": []}
            for cg in mi.findall("p:ColumnGroup", NS):
                cols[cg.get("Usage")] = [c.get("Name").strip("[]") for c in cg.findall("p:Column", NS)]
            found["missing_indexes"].append({
                "table": (mi.get("Table") or "").strip("[]"), "impact": float(group.get("Impact") or 0), **cols,
            })
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--server", default="localhost,1433")
    ap.add_argument("--database", required=True)
    ap.add_argument("--top", type=int, default=30)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    rows = top_queries(args.server, args.database, args.top)
    for row in rows:
        row["plan"] = issues(plan_xml(args.server, args.database, row["plan_id"]))
    pathlib.Path(args.out).write_text(json.dumps(rows, indent=2), encoding="utf-8")

    for row in rows:
        plan = row["plan"]
        big = [s for s in plan["scans"] if s["table_rows"] >= 100_000]
        flags = []
        if big:
            flags.append("SCAN " + ", ".join(f"{s['table']}({int(s['table_rows'])})" for s in big))
        if plan["key_lookups"]:
            flags.append("LOOKUP " + ", ".join(sorted({k["table"] for k in plan["key_lookups"]})))
        if plan["sorts"]:
            flags.append(f"SORT x{plan['sorts']}")
        if plan["missing_indexes"]:
            flags.append("MISSING " + ", ".join(sorted({m["table"] for m in plan["missing_indexes"]})))
        print(f"d{row['by_duration']:>3} r{row['by_reads']:>3} q{row['query_id']:>6} n={row['executions']:>5} "
              f"avg={row['avg_ms']:>8.2f}ms reads={row['avg_reads']:>8} | {'; '.join(flags) or '-'}")


if __name__ == "__main__":
    main()
