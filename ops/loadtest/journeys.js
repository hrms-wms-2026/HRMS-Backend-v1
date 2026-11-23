// Crownus API load test: the main user journeys, as k6 (task 29). Profiles and the release gate:
// docs/deployment/release-gate.md. How to run: ops/loadtest/README.md.
//
//   PROFILE     smoke | normal | peak | soak             (default smoke)
//   ROOT_URL    https://<root host>        sign-in host, e.g. https://crownus.test:7229
//   TENANT_URL  https://<tenant>.<root>    e.g. https://dapi.crownus.test:7229
//   USERS       comma-separated sign-in emails of test accounts (one per virtual user, reused round robin)
//   PASSWORD    their password (test accounts only, never a real user's)
//   NORMAL_VUS  expected concurrent users (default 20); peak = 3x
//   SOAK_MINUTES  length of the soak plateau (default 60)
//   ACCEPT_TEST_LEGAL=1  accept pending legal documents during sign-in. Test accounts only.
//   TENANT_HEADER  tenant slug, for single-host mode (TENANT_URL = ROOT_URL)
//
// Each virtual user signs in once (setup), then repeats a session: dashboard, task board, employee list,
// attendance today, leave balances, inbox. Every request carries a "name" tag (the route template) so the
// summary shows p95 per endpoint, and a "journey" tag for the gate. Writes (clock in, submit a leave request)
// are not included yet: see the release-gate doc.
import http from 'k6/http';
import { check, group, sleep, fail } from 'k6';

const PROFILE = __ENV.PROFILE || 'smoke';
const ROOT = (__ENV.ROOT_URL || '').replace(/\/$/, '');
const TENANT = (__ENV.TENANT_URL || '').replace(/\/$/, '');
const USERS = (__ENV.USERS || '').split(',').map((u) => u.trim()).filter(Boolean);
const PASSWORD = __ENV.PASSWORD || '';
const NORMAL = parseInt(__ENV.NORMAL_VUS || '20', 10);
// Single-host mode (App Service test setup): one host for everything, the tenant comes from this header.
const TENANT_HEADERS = __ENV.TENANT_HEADER ? { 'X-Crownus-Tenant': __ENV.TENANT_HEADER } : {};

const profiles = {
  smoke: { vus: 1, iterations: 2 },
  normal: { stages: [{ duration: '1m', target: NORMAL }, { duration: '5m', target: NORMAL }, { duration: '30s', target: 0 }] },
  peak: { stages: [{ duration: '2m', target: NORMAL * 3 }, { duration: '5m', target: NORMAL * 3 }, { duration: '30s', target: 0 }] },
  soak: { stages: [{ duration: '2m', target: NORMAL }, { duration: `${__ENV.SOAK_MINUTES || 60}m`, target: NORMAL }, { duration: '1m', target: 0 }] },
};
if (!profiles[PROFILE]) fail(`Unknown PROFILE ${PROFILE}`);

// Release gate (docs/deployment/release-gate.md): p95 <= 200 ms per journey at normal load, < 1 % errors at peak.
// Smoke is a correctness check after a deploy, with a loose latency bound for a cold instance.
const gate = {
  smoke: { checks: ['rate==1'], http_req_failed: ['rate==0'], http_req_duration: ['p(95)<2000'] },
  normal: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{journey:dashboard}': ['p(95)<200'],
    'http_req_duration{journey:taskboard}': ['p(95)<200'],
    'http_req_duration{journey:employees}': ['p(95)<200'],
    'http_req_duration{journey:attendance}': ['p(95)<200'],
    'http_req_duration{journey:leave}': ['p(95)<200'],
    'http_req_duration{journey:inbox}': ['p(95)<200'],
  },
  peak: { http_req_failed: ['rate<0.01'] },
  soak: { http_req_failed: ['rate<0.01'], http_req_duration: ['p(95)<400'] },
};

// k6 only reports a tag's numbers when a threshold names it: one always-true threshold per endpoint gives the
// per-endpoint p95 in every summary.
const ENDPOINTS = [
  'GET /auth/me', 'GET /dashboards/me', 'GET /dashboards/{id}/bootstrap', 'GET /notifications/unread-count',
  'GET /inbox/counts', 'GET /work/projects/mine', 'GET /work/projects/{id}/work-graph', 'GET /employees',
  'GET /attendance/time-tracking/today', 'GET /leave/balances/my', 'GET /inbox/chats',
];
const JOURNEYS = ['dashboard', 'taskboard', 'employees', 'attendance', 'leave', 'inbox'];
const perEndpoint = Object.fromEntries([
  ...ENDPOINTS.map((n) => [`http_req_duration{name:${n}}`, ['max>=0']]),
  ...JOURNEYS.map((j) => [`http_req_duration{journey:${j}}`, ['max>=0']]),
]);

export const options = {
  ...profiles[PROFILE],
  setupTimeout: '10m',
  thresholds: { ...perEndpoint, ...gate[PROFILE] },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// A distinct client address per virtual user, as real users behind a proxy have. The API trusts it only from
// configured proxies (ForwardedHeaders), so against UAT the load balancer's own address decides.
const clientIp = (n) => `10.60.${(n >> 8) & 255}.${(n & 255) || 1}`;

const exchangeCode = (res) => {
  const match = /[?#&]code=([^&]+)/.exec((res.status === 202 && res.json('continue_url')) || '');
  return match ? decodeURIComponent(match[1]) : null;
};

function signIn(email, n) {
  // A fresh cookie jar per account: one account's session or legal cookies must not reach the next sign-in.
  const jar = http.cookieJar();
  jar.clear(ROOT);
  jar.clear(TENANT);
  const headers = { 'Content-Type': 'application/json', 'X-Forwarded-For': clientIp(n), Origin: TENANT };
  let login = http.post(`${ROOT}/api/v1/auth/login`, JSON.stringify({ email, password: PASSWORD }),
    { headers, tags: { name: 'POST /auth/login', journey: 'signin' } });

  // Test accounts that have never signed in still owe the legal documents. Only with ACCEPT_TEST_LEGAL=1, and only
  // for test accounts, accept them through the app's own sign-in step, as the browser does.
  if (login.status === 202 && login.json('legal_acceptance_required') === true && __ENV.ACCEPT_TEST_LEGAL === '1') {
    const pending = login.json('pending_legal_documents') || [];
    const csrf = (login.cookies.crownus_legal_csrf || [])[0];
    login = http.post(login.json('continue_url'), JSON.stringify({
      acceptances: pending.map((d) => ({
        document_type: d.document_type, version: d.version, decision: 'accepted', content_hash: d.content_hash,
      })),
    }), { headers: { ...headers, 'X-CSRF-Token': csrf ? csrf.value : '' }, tags: { name: 'POST /legal/acceptances/complete-login', journey: 'signin' } });
  }

  const code = exchangeCode(login);
  if (!code) return null;
  const exchange = http.post(`${TENANT}/api/v1/auth/session-exchange`, JSON.stringify({ code }),
    { headers: { ...headers, ...TENANT_HEADERS }, tags: { name: 'POST /auth/session-exchange', journey: 'signin' } });
  if (exchange.status !== 200) return null;
  const cookies = exchange.cookies;
  return Object.keys(cookies).map((k) => `${k}=${cookies[k][0].value}`).join('; ');
}

export function setup() {
  if (!ROOT || !TENANT || USERS.length === 0) fail('ROOT_URL, TENANT_URL and USERS are required');
  const sessions = [];
  USERS.forEach((email, i) => {
    const cookie = signIn(email, i + 1);
    if (cookie) sessions.push(cookie);
  });
  if (sessions.length === 0) fail('No test account could sign in');
  console.log(`Signed in ${sessions.length} of ${USERS.length} test accounts.`);
  return { sessions };
}

function get(path, name, journey, cookie) {
  const res = http.get(`${TENANT}/api/v1${path}`, {
    headers: { Cookie: cookie, 'X-Forwarded-For': clientIp(__VU), Accept: 'application/json', ...TENANT_HEADERS },
    tags: { name, journey },
  });
  check(res, { [`${name} is 2xx`]: (r) => r.status >= 200 && r.status < 300 });
  return res;
}

const think = () => sleep(PROFILE === 'smoke' ? 0 : 1 + Math.random() * 2);

export default function (data) {
  const cookie = data.sessions[(__VU - 1) % data.sessions.length];

  group('dashboard', () => {
    get('/auth/me', 'GET /auth/me', 'dashboard', cookie);
    const dashboards = get('/dashboards/me', 'GET /dashboards/me', 'dashboard', cookie);
    const id = dashboards.status === 200 ? (dashboards.json('id') || dashboards.json('0.id')) : null;
    if (id) get(`/dashboards/${id}/bootstrap`, 'GET /dashboards/{id}/bootstrap', 'dashboard', cookie);
    get('/notifications/unread-count', 'GET /notifications/unread-count', 'dashboard', cookie);
    get('/inbox/counts', 'GET /inbox/counts', 'dashboard', cookie);
  });
  think();

  group('task board', () => {
    const projects = get('/work/projects/mine', 'GET /work/projects/mine', 'taskboard', cookie);
    let id = null;
    if (projects.status === 200) {
      const body = projects.json();
      const list = Array.isArray(body) ? body : (body.items || []);
      id = list.length ? list[0].id : null;
    }
    if (id) get(`/work/projects/${id}/work-graph`, 'GET /work/projects/{id}/work-graph', 'taskboard', cookie);
  });
  think();

  group('employee list', () => {
    get('/employees?page=1&pageSize=25', 'GET /employees', 'employees', cookie);
  });
  think();

  group('attendance', () => {
    get('/attendance/time-tracking/today', 'GET /attendance/time-tracking/today', 'attendance', cookie);
  });
  think();

  group('leave', () => {
    get(`/leave/balances/my?year=${new Date().getUTCFullYear()}`, 'GET /leave/balances/my', 'leave', cookie);
  });
  think();

  group('inbox', () => {
    get('/inbox/counts', 'GET /inbox/counts', 'inbox', cookie);
    get('/inbox/chats', 'GET /inbox/chats', 'inbox', cookie);
  });
  think();
}

export function handleSummary(data) {
  const out = __ENV.SUMMARY_DIR ? `${__ENV.SUMMARY_DIR}/${PROFILE}-summary.json` : null;
  const result = { stdout: textSummary(data) };
  // setup_data holds the test accounts' session cookies: never write it out.
  const { setup_data: _sessions, ...safe } = data;
  if (out) result[out] = JSON.stringify(safe, null, 2);
  return result;
}

// Short text summary: per-endpoint p95 and errors, then the gate.
function textSummary(data) {
  const lines = [`\nProfile: ${PROFILE}`];
  const m = data.metrics;
  const rows = Object.keys(m).filter((k) => k.startsWith('http_req_duration{name:')).sort();
  for (const key of rows) {
    const v = m[key].values;
    lines.push(`  ${key.slice(23, -1).padEnd(40)} p95 ${v['p(95)'].toFixed(0).padStart(6)} ms   med ${v.med.toFixed(0).padStart(6)} ms`);
  }
  const failed = m.http_req_failed ? (m.http_req_failed.values.rate * 100).toFixed(2) : 'n/a';
  lines.push(`  requests ${m.http_reqs.values.count}, ${m.http_reqs.values.rate.toFixed(1)}/s, failed ${failed} %`);
  for (const [name, t] of Object.entries(m)) {
    if (t.thresholds) for (const [rule, r] of Object.entries(t.thresholds)) lines.push(`  ${r.ok ? 'PASS' : 'FAIL'} ${name} ${rule}`);
  }
  return lines.join('\n') + '\n';
}
