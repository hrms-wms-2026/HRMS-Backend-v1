// HRMS backend load runner (Node, no extra deps). Mirrors k6-dashboard-load.js.
// Simulates users opening the dashboard / WM pages: each virtual user (VU) runs the
// page-load request mix, then "thinks" 1-3 s, and repeats. VUs ramp like k6 stages.
//
// Env:
//   BASE        https://dapi.crownus.test:7229/api/v1
//   COOKIE      tenant session cookie header (onevo_session=...; onevo_csrf=...)
//   DASH_ID     a dashboard id from GET /dashboards/me
//   PROJECT_ID  a project id from GET /work/projects/mine
//   OUT         path for the JSON summary
//   START_DELAY seconds to wait before applying load (baseline window)
//   INSECURE=1  accept the local mkcert dev cert without NODE_EXTRA_CA_CERTS
//
// Each VU sends its own X-Forwarded-For (10.50.x.y). Loopback is a trusted proxy by
// default in ASP.NET Core, so every VU counts as a separate client IP for the
// per-IP rate limiter — the same as real users behind nginx in production.

import https from 'node:https';
import fs from 'node:fs';

const BASE = process.env.BASE;
const COOKIE = process.env.COOKIE;
const DASH_ID = process.env.DASH_ID;
const PROJECT_ID = process.env.PROJECT_ID;
const OUT = process.env.OUT ?? 'loadtest-summary.json';
const START_DELAY = Number(process.env.START_DELAY ?? 0);

const STAGES = [
  { dur: 60, target: 10 },
  { dur: 60, target: 25 },
  { dur: 120, target: 50 },
  { dur: 120, target: 100 },
  { dur: 30, target: 0 },
];

const ENDPOINTS = [
  ['auth/me', '/auth/me'],
  ['dashboards/me', '/dashboards/me'],
  ['dashboards/{id}/bootstrap', `/dashboards/${DASH_ID}/bootstrap`],
  ['notifications/unread-count', '/notifications/unread-count'],
  ['monitoring/activation/status', '/monitoring/activation/status'],
  ['notifications', '/notifications'],
  ['work/my-weekly-progress', '/work/my-weekly-progress'],
  ['dashboard/team/capabilities', '/dashboard/team/capabilities'],
  ['work/projects/mine', '/work/projects/mine'],
  ['work/projects/{id}/work-graph', `/work/projects/${PROJECT_ID}/work-graph`],
];

const total = STAGES.reduce((s, x) => s + x.dur, 0);
const stats = {};          // endpoint -> { lat: [], codes: {} }
const byLevel = {};        // stage target -> { lat: [], n, err, r429, start, end }
const timeline = [];       // 10 s buckets
let bucket = { n: 0, err: 0, r429: 0, lat: [] };

function targetAt(t) {
  let prevTarget = 0, acc = 0;
  for (const st of STAGES) {
    if (t < acc + st.dur) return { vus: Math.round(prevTarget + (st.target - prevTarget) * ((t - acc) / st.dur)), stage: st.target };
    acc += st.dur; prevTarget = st.target;
  }
  return { vus: 0, stage: 0 };
}

function pct(arr, p) {
  if (!arr.length) return null;
  const s = [...arr].sort((a, b) => a - b);
  return +s[Math.min(s.length - 1, Math.floor((p / 100) * s.length))].toFixed(1);
}

function request(agent, ip, path) {
  return new Promise((resolve) => {
    const t0 = performance.now();
    const req = https.request(BASE + path, {
      method: 'GET', agent,
      headers: { Cookie: COOKIE, 'X-Forwarded-For': ip, Accept: 'application/json', 'User-Agent': 'hrms-loadtest' },
      timeout: 30000,
    }, (res) => {
      res.resume();
      res.on('end', () => resolve({ code: res.statusCode, ms: performance.now() - t0 }));
    });
    req.on('timeout', () => req.destroy(new Error('timeout')));
    req.on('error', () => resolve({ code: 0, ms: performance.now() - t0 }));
    req.end();
  });
}

let startTs = 0;
let activeVus = 0;
const vuRunning = new Map();

function record(name, code, ms, stage) {
  const e = (stats[name] ??= { lat: [], codes: {} });
  e.lat.push(ms); e.codes[code] = (e.codes[code] ?? 0) + 1;
  const l = (byLevel[stage] ??= { lat: [], n: 0, err: 0, r429: 0, start: Date.now(), end: 0 });
  l.lat.push(ms); l.n++; l.end = Date.now();
  const bad = code === 0 || code >= 500 || (code >= 400 && code !== 429);
  if (bad) l.err++; if (code === 429) l.r429++;
  bucket.n++; bucket.lat.push(ms); if (bad) bucket.err++; if (code === 429) bucket.r429++;
}

async function vu(id) {
  const agent = new https.Agent({ keepAlive: true, maxSockets: 6, rejectUnauthorized: process.env.INSECURE !== '1' });
  const ip = `10.50.${(id >> 8) & 255}.${(id & 255) || 1}`;
  while (vuRunning.get(id)) {
    for (const [name, path] of ENDPOINTS) {
      if (!vuRunning.get(id)) break;
      const t = (Date.now() - startTs) / 1000;
      const { code, ms } = await request(agent, ip, path);
      record(name, code, ms, targetAt(t).stage);
    }
    await new Promise((r) => setTimeout(r, 1000 + Math.random() * 2000));
  }
  agent.destroy();
}

async function main() {
  if (START_DELAY > 0) {
    console.log(`baseline window: waiting ${START_DELAY}s before load`);
    await new Promise((r) => setTimeout(r, START_DELAY * 1000));
  }
  startTs = Date.now();
  console.log(`load start ${new Date().toISOString()}`);
  let nextId = 1;
  const ticker = setInterval(() => {
    const t = (Date.now() - startTs) / 1000;
    const { vus } = targetAt(t);
    while (activeVus < vus) { const id = nextId++; vuRunning.set(id, true); activeVus++; vu(id); }
    while (activeVus > vus) { const id = [...vuRunning.keys()].find((k) => vuRunning.get(k)); vuRunning.set(id, false); activeVus--; }
  }, 250);
  const reporter = setInterval(() => {
    const t = Math.round((Date.now() - startTs) / 1000);
    const row = { t, vus: activeVus, rps: +(bucket.n / 10).toFixed(1), p50: pct(bucket.lat, 50), p95: pct(bucket.lat, 95), err: bucket.err, r429: bucket.r429 };
    timeline.push(row);
    console.log(JSON.stringify(row));
    bucket = { n: 0, err: 0, r429: 0, lat: [] };
  }, 10000);
  await new Promise((r) => setTimeout(r, total * 1000));
  clearInterval(ticker);
  for (const k of vuRunning.keys()) vuRunning.set(k, false);
  await new Promise((r) => setTimeout(r, 5000));
  clearInterval(reporter);

  const allLat = Object.values(stats).flatMap((e) => e.lat);
  const n = allLat.length;
  const codes = {};
  for (const e of Object.values(stats)) for (const [c, k] of Object.entries(e.codes)) codes[c] = (codes[c] ?? 0) + k;
  const r429 = codes['429'] ?? 0;
  const errors = Object.entries(codes).filter(([c]) => c === '0' || +c >= 500 || (+c >= 400 && c !== '429')).reduce((s, [, k]) => s + k, 0);
  const summary = {
    started: new Date(startTs).toISOString(), durationS: total,
    requests: n, rps: +(n / total).toFixed(1), errors, errorRate: +(errors / n * 100).toFixed(2), r429, codes,
    overall: { p50: pct(allLat, 50), p95: pct(allLat, 95), p99: pct(allLat, 99), max: pct(allLat, 100) },
    endpoints: Object.fromEntries(Object.entries(stats).map(([k, e]) => [k, { n: e.lat.length, p50: pct(e.lat, 50), p95: pct(e.lat, 95), p99: pct(e.lat, 99), max: pct(e.lat, 100), codes: e.codes }])),
    byStageTarget: Object.fromEntries(Object.entries(byLevel).map(([k, l]) => [k, { n: l.n, rps: +(l.n / Math.max(1, (l.end - l.start) / 1000)).toFixed(1), p50: pct(l.lat, 50), p95: pct(l.lat, 95), p99: pct(l.lat, 99), err: l.err, r429: l.r429 }])),
    timeline,
  };
  fs.writeFileSync(OUT, JSON.stringify(summary, null, 2));
  console.log(`done: ${n} requests, ${summary.rps} rps, p95 ${summary.overall.p95} ms, errors ${errors}, 429 ${r429}`);
  process.exit(0);
}

main();
