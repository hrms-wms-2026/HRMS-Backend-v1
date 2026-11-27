"""Task 06 Part B: time every tenant GET endpoint against a running API and write the baseline table.

Calls run one at a time. For each endpoint: one warm-up call, then --runs timed calls. The server time and SQL count
come from the API's request log line (task 06 Part A), which this script reads from the API's console log file:

    HTTP GET /api/v1/... responded 200 in 12.3 ms (5 SQL, 3.2 ms)

Route parameters are filled from earlier responses: `{id}` from the list endpoint just above it, and named ids such
as `{employeeId}` from any response field with that name. Routes it cannot fill are listed as skipped.

Standard library only. Usually started by run-baseline.ps1, which also starts and stops the API.
"""
import argparse
import http.cookiejar
import json
import os
import re
import ssl
import statistics
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter, defaultdict
from datetime import date, datetime, timedelta, timezone

LOG_LINE = re.compile(
    r"HTTP GET (?P<route>\S+) responded (?P<status>\d+) in (?P<ms>[\d.]+) ms \((?P<sql>\d+) SQL, (?P<sqlms>[\d.]+) ms\)")
PARAM = re.compile(r"\{(\w+)(?::[^}]*)?\}")
START, END = "<!-- baseline:start -->", "<!-- baseline:end -->"

# Not timed: they redirect to another site, stream without end, or start a sign-in or file flow.
SKIP = re.compile(r"(callback|oauth|/authorize|/connect\b|/stream|/sse|/events$|/download|/export|/token)", re.I)


def parse_args():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--openapi", default="docs/api/openapi/tenant-v1.json")
    p.add_argument("--log", help="the API's console log file (required unless --from-json)")
    p.add_argument("--root-url", default="https://crownus.test:7339", help="sign-in host")
    p.add_argument("--tenant-url", default="https://dapi.crownus.test:7339", help="tenant host")
    p.add_argument("--origin", default="https://dapi.crownus.test:4200", help="Origin sent on session exchange")
    p.add_argument("--runs", type=int, default=20)
    p.add_argument("--out", default="docs/reports/performance/2026-11-baseline.md")
    p.add_argument("--json", default="docs/reports/performance/2026-11-baseline.json")
    p.add_argument("--only", help="regex: time only matching routes (for a quick check)")
    p.add_argument("--insecure", action="store_true", help="skip TLS checks (local mkcert certificates)")
    p.add_argument("--from-json", help="rewrite the report table from a saved run instead of calling the API")
    return p.parse_args()


class Client:
    def __init__(self, args):
        ctx = ssl.create_default_context()
        if args.insecure:
            ctx.check_hostname = False
            ctx.verify_mode = ssl.CERT_NONE
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPSHandler(context=ctx), urllib.request.HTTPCookieProcessor(self.jar))
        self.args = args

    def request(self, method, url, body=None, headers=None, timeout=60):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method, headers={"Accept": "application/json", **(headers or {})})
        if data is not None:
            req.add_header("Content-Type", "application/json")
        started = time.perf_counter()
        try:
            with self.opener.open(req, timeout=timeout) as resp:
                payload = resp.read()
                status = resp.status
        except urllib.error.HTTPError as e:
            payload, status = e.read(), e.code
        except (urllib.error.URLError, TimeoutError) as e:
            return 0, None, (time.perf_counter() - started) * 1000, str(e)
        elapsed = (time.perf_counter() - started) * 1000
        try:
            parsed = json.loads(payload) if payload else None
        except ValueError:
            parsed = None
        return status, parsed, elapsed, None

    def sign_in(self):
        email, password = os.environ.get("BASELINE_EMAIL"), os.environ.get("BASELINE_PASSWORD")
        if not email or not password:
            sys.exit("Set BASELINE_EMAIL and BASELINE_PASSWORD to a seeded test account (never a real person's).")
        status, body, _, err = self.request("POST", f"{self.args.root_url}/api/v1/auth/login",
                                            {"email": email, "password": password})
        if status not in (200, 202) or not body or "continue_url" not in body:
            sys.exit(f"Sign-in failed: {status} {err or body}")
        code = urllib.parse.parse_qs(urllib.parse.urlparse(body["continue_url"]).query)["code"][0]
        status, body, _, err = self.request("POST", f"{self.args.tenant_url}/api/v1/auth/session-exchange",
                                            {"code": code}, {"Origin": self.args.origin})
        if status not in (200, 204):
            sys.exit(f"Session exchange failed: {status} {err or body}")


def normalize(route):
    """The log has the raw route pattern (`api/v1/x/{id:guid}`); OpenAPI has `/api/v1/x/{id}`."""
    return PARAM.sub(lambda m: "{" + m.group(1).lower() + "}", route.strip("/").lower())


class LogTail:
    """Reads request log lines the API wrote since the last read."""

    def __init__(self, path):
        self.path = path
        self.offset = os.path.getsize(path)

    def lines_for(self, route, wait=3.0):
        deadline = time.time() + wait
        buffer = ""
        while True:
            with open(self.path, "r", encoding="utf-8", errors="ignore") as f:
                f.seek(self.offset)
                chunk = f.read()
                self.offset = f.tell()
            buffer += chunk
            hits = [m for m in LOG_LINE.finditer(buffer) if normalize(m.group("route")) == normalize(route)]
            if hits or time.time() > deadline:
                return hits
            time.sleep(0.02)


def collect_values(node, into, key=None):
    """Remembers every id-like value in a response, by field name."""
    if isinstance(node, dict):
        for k, v in node.items():
            collect_values(v, into, k)
    elif isinstance(node, list):
        for v in node[:50]:
            collect_values(v, into, key)
    elif key and isinstance(node, (str, int)) and not isinstance(node, bool):
        lk = key.lower()
        if lk == "id" or lk.endswith("id") or lk in ("key", "kind", "source", "version", "documenttype", "provider"):
            into[lk][str(node)] += 1


def first_item_id(body):
    items = body
    if isinstance(body, dict):
        for k in ("items", "data", "results", "rows", "value"):
            if isinstance(body.get(k), list):
                items = body[k]
                break
    if isinstance(items, list):
        for item in items:
            if isinstance(item, dict) and item.get("id") is not None:
                return str(item["id"])
    if isinstance(body, dict) and body.get("id") is not None:
        return str(body["id"])
    return None


def resolve(path, bodies, values):
    """Fills each route parameter, or returns the first one it cannot fill."""
    out = path
    for m in PARAM.finditer(path):
        name = m.group(1)
        value = None
        if name == "id":
            parent = path[:m.start()].rstrip("/")
            filled_parent = resolve(parent, bodies, values)[0] if "{" in parent else parent
            if filled_parent and filled_parent in bodies:
                value = first_item_id(bodies[filled_parent])
            if value is None:
                # No usable list: `/projects/{id}` takes a `projectId` seen in another response.
                segment = parent.rsplit("/", 1)[-1].lower()
                singular = segment[:-3] + "y" if segment.endswith("ies") else segment.rstrip("s")
                seen = values.get(singular.replace("-", "") + "id")
                if not seen:
                    return None, "id (no list rows and no matching id field)"
                value = seen.most_common(1)[0][0]
        if value is None:
            seen = values.get(name.lower())
            if seen:
                value = seen.most_common(1)[0][0]
        if value is None:
            return None, name
        out = out.replace(m.group(0), urllib.parse.quote(value, safe=""), 1)
    return out, None


def default_query(op, today, values, with_ids=False):
    """What the screens send: the last 30 days ending today, this year and month, and the id filters many reads
    need although the API description marks them optional (the first id of that name seen so far)."""
    week_start = today - timedelta(days=today.weekday())
    pairs = []
    for prm in op.get("parameters", []):
        if prm.get("in") != "query":
            continue
        fmt_ = (prm.get("schema") or {}).get("format")
        name = prm["name"].lower()
        if with_ids and fmt_ == "uuid" and name.endswith("id") and values.get(name):
            pairs.append((prm["name"], values[name].most_common(1)[0][0]))
            continue
        if name in ("year", "month"):
            pairs.append((prm["name"], str(today.year if name == "year" else today.month)))
            continue
        if fmt_ not in ("date", "date-time"):
            continue
        if name in ("from", "fromdate", "start", "startdate"):
            value = today - timedelta(days=30)
        elif name in ("to", "todate", "end", "enddate"):
            value = today
        elif name == "weekstart":
            value = week_start
        elif name in ("date", "anchor", "day"):
            value = today
        else:
            continue
        text = value.isoformat() if fmt_ == "date" else f"{value.isoformat()}T00:00:00Z"
        pairs.append((prm["name"], text))
    return ("?" + urllib.parse.urlencode(pairs)) if pairs else ""


def percentile(values, p):
    if not values:
        return None
    s = sorted(values)
    return s[min(len(s) - 1, int(round((len(s) - 1) * p)))]


def time_route(client, tail, args, template, url):
    status, body, _, err = client.request("GET", url)   # warm-up
    tail.lines_for(template)
    if err or status >= 400 or status == 0:
        detail = err
        if detail is None and isinstance(body, dict):
            detail = body.get("detail") or body.get("title") or json.dumps(body.get("errors"))[:200]
        return {"route": template, "url": url, "status": status, "error": detail}, body
    server, client_ms, sql, sql_ms = [], [], [], []
    for _ in range(args.runs):
        status, _, elapsed, err = client.request("GET", url)
        lines = tail.lines_for(template)
        client_ms.append(elapsed)
        if lines:
            m = lines[-1]
            server.append(float(m.group("ms")))
            sql.append(int(m.group("sql")))
            sql_ms.append(float(m.group("sqlms")))
    row = {
        "route": template, "url": url, "status": status,
        "p50": percentile(server, .5), "p95": percentile(server, .95), "max": max(server) if server else None,
        "client_p50": percentile(client_ms, .5),
        "sql": int(statistics.median(sql)) if sql else None,
        "sql_ms": percentile(sql_ms, .5), "samples": len(server),
    }
    return row, body


def main():
    args = parse_args()
    if args.from_json:
        saved = json.load(open(args.from_json, encoding="utf-8"))
        args.runs = saved["runs"]
        write_outputs(args, saved["rows"], saved["skipped"], saved["run"])
        return
    if not args.log:
        sys.exit("--log is required")
    spec = json.load(open(args.openapi, encoding="utf-8"))
    operations = {p: item["get"] for p, item in spec["paths"].items() if "get" in item}
    templates = sorted(operations)
    today = date.today()
    if args.only:
        templates = [t for t in templates if re.search(args.only, t)]

    client = Client(args)
    client.sign_in()
    tail = LogTail(args.log)

    bodies, values = {}, defaultdict(Counter)
    rows, skipped = [], []
    pending = [t for t in templates if not SKIP.search(t)]
    skipped += [{"route": t, "reason": "not timed (redirect, stream or file flow)"} for t in templates if SKIP.search(t)]

    # Parameter-free routes first, then keep filling routes as their parameters become known.
    while pending:
        progressed, waiting = False, []
        for template in pending:
            url_path, missing = resolve(template, bodies, values)
            if missing:
                waiting.append((template, missing))
                continue
            query = default_query(operations[template], today, values)
            row, body = time_route(client, tail, args, template, args.tenant_url + url_path + query)
            with_ids = default_query(operations[template], today, values, with_ids=True)
            if row.get("status") == 400 and with_ids != query:
                # A filter the API needs although its description marks it optional.
                row, body = time_route(client, tail, args, template, args.tenant_url + url_path + with_ids)
                row["note"] = "needs an id filter the API description marks optional"
            rows.append(row)
            bodies[url_path] = body
            collect_values(body, values)
            progressed = True
            print(f"{row.get('p95') or '-':>8} ms  {row['status']}  {template}", flush=True)
        pending = [t for t, _ in waiting]
        if not progressed:
            skipped += [{"route": t, "reason": f"no value for {m}"} for t, m in waiting]
            break

    write_outputs(args, rows, skipped)


def fmt(v):
    return "-" if v is None else f"{v:.0f}"


def write_outputs(args, rows, skipped, stamp=None):
    # Sorted by p50: with one caller and background jobs running, a single slow call can lift p95 of 20 calls.
    timed = sorted((r for r in rows if r.get("p50") is not None), key=lambda r: -r["p50"])
    failed = [r for r in rows if r.get("p50") is None]
    over50 = [r for r in timed if r["p50"] > 200]
    over95 = [r for r in timed if r["p95"] > 200]
    stamp = stamp or datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")

    lines = [START, "",
             f"Run {stamp}: {len(timed)} endpoints timed ({args.runs} warm calls each), {len(failed)} failed, "
             f"{len(skipped)} skipped. **{len(over50)} have p50 over 200 ms; {len(over95)} have p95 over 200 ms.** "
             "Sorted by p50. *Spiky*: p95 over 200 ms and more than 3 times p50 (usually a background job "
             "running at the same moment).", "",
             "| # | Route | p50 ms | p95 ms | max ms | SQL | SQL ms | Client p50 ms |",
             "|---|---|---|---|---|---|---|---|"]
    for i, r in enumerate(timed, 1):
        mark = " **(top 20)**" if i <= 20 else ""
        if r["p95"] > 200 and r["p95"] > 3 * r["p50"]:
            mark += " *spiky*"
        if r.get("note"):
            mark += " †"
        lines.append(f"| {i} | `{r['route']}`{mark} | {fmt(r['p50'])} | {fmt(r['p95'])} | {fmt(r['max'])} | "
                     f"{r['sql'] if r['sql'] is not None else '-'} | {fmt(r['sql_ms'])} | {fmt(r['client_p50'])} |")
    if any(r.get("note") for r in timed):
        lines += ["", "† Answers 400 without an id filter that the API description marks optional; timed with one."]
    if failed:
        lines += ["", "### Failed calls", "", "| Route | Status | Error |", "|---|---|---|"]
        lines += [f"| `{r['route']}` | {r['status']} | {(r.get('error') or '').replace('|', '/')} |" for r in failed]
    if skipped:
        lines += ["", "### Skipped", "", "| Route | Why |", "|---|---|"]
        lines += [f"| `{s['route']}` | {s['reason']} |" for s in skipped]
    lines += ["", END]
    block = "\n".join(lines)

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    if os.path.exists(args.out):
        text = open(args.out, encoding="utf-8").read()
        if START in text and END in text:
            text = text[:text.index(START)] + block + text[text.index(END) + len(END):]
        else:
            text = text.rstrip() + "\n\n" + block + "\n"
    else:
        text = "# Endpoint timing baseline\n\n" + block + "\n"
    open(args.out, "w", encoding="utf-8", newline="\n").write(text)
    if not getattr(args, "from_json", None):
        json.dump({"run": stamp, "runs": args.runs, "rows": rows, "skipped": skipped},
                  open(args.json, "w", encoding="utf-8"), indent=1)
    print(f"\n{len(timed)} timed, {len(failed)} failed, {len(skipped)} skipped, {len(over50)} with p50 over 200 ms -> {args.out}")


if __name__ == "__main__":
    main()
