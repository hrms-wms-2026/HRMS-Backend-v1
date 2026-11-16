// k6 version of loadtest-runner.mjs — same request mix and stages.
// Run (after installing k6):
//   k6 run -e BASE=https://dapi.crownus.test:7229/api/v1 -e COOKIE="onevo_session=...; onevo_csrf=..." \
//          -e DASH_ID=<id> -e PROJECT_ID=<id> --insecure-skip-tls-verify k6-dashboard-load.js
// Get COOKIE by logging in once in the browser (DevTools > Application > Cookies) on the tenant host.
// Each VU sends its own X-Forwarded-For so the per-IP limiter (300 req/min on /api/v1) treats
// it as a separate client, like real users behind a reverse proxy.
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '1m', target: 10 },
    { duration: '1m', target: 25 },
    { duration: '2m', target: 50 },
    { duration: '2m', target: 100 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],
    'http_req_failed{expected:true}': ['rate<0.01'],
  },
};

const BASE = __ENV.BASE;
const ENDPOINTS = [
  ['auth/me', '/auth/me'],
  ['dashboards/me', '/dashboards/me'],
  ['dashboards/{id}/bootstrap', `/dashboards/${__ENV.DASH_ID}/bootstrap`],
  ['notifications/unread-count', '/notifications/unread-count'],
  ['monitoring/activation/status', '/monitoring/activation/status'],
  ['notifications', '/notifications'],
  ['work/my-weekly-progress', '/work/my-weekly-progress'],
  ['dashboard/team/capabilities', '/dashboard/team/capabilities'],
  ['work/projects/mine', '/work/projects/mine'],
  ['work/projects/{id}/work-graph', `/work/projects/${__ENV.PROJECT_ID}/work-graph`],
];

export default function () {
  const ip = `10.50.${(__VU >> 8) & 255}.${(__VU & 255) || 1}`;
  const params = (name) => ({
    headers: { Cookie: __ENV.COOKIE, 'X-Forwarded-For': ip, Accept: 'application/json' },
    tags: { name, expected: 'true' },
    responseCallback: http.expectedStatuses(200, 429),
  });
  for (const [name, path] of ENDPOINTS) {
    const res = http.get(BASE + path, params(name));
    check(res, { 'status 200': (r) => r.status === 200, 'not rate-limited': (r) => r.status !== 429 });
  }
  sleep(1 + Math.random() * 2);
}
