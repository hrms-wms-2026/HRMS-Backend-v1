#!/usr/bin/env node
// Postgres Schema Snapshot generator - implements SPEC.md.
//
//   node schema-snapshots/generate.mjs            # uses .env ONEVO_DB_* (default DB: OnevoDb)
//   ONEVO_DB_NAME=OnevoDb_other node schema-snapshots/generate.mjs
//
// Produces, under schema-snapshots/ :
//   raw/<migration_id>/{meta,columns,constraints}.json   - SPEC.md section 9.1
//   <migration_id>-<YYYY-MM-DD>/<domain>.md               - one per domain, SPEC.md section 3-7
//   <migration_id>-<YYYY-MM-DD>/viewer.html               - the browsable page, SPEC.md section 12
//
// Requires: PostgreSQL client (`psql`) + a database migrated with `dotnet ef database update`.
// No npm dependencies.

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, '..');
const SQL_FILE = join(HERE, 'schema-dump.sql');

// --- Domain buckets (SPEC.md section 1/8: rules here, not a hand-kept table list). ----------
// First matching rule wins; ordering matters. A table whose name prefix matches nothing lands
// in "Other" - add a rule and rerun. Counts are asserted against SPEC.md section 8 after.
const DOMAINS = [
  ['Auth & Identity', /^(users?|roles?|permissions?|role_|permission_|user_|sessions?|platform_user_sessions|mfa_|password_|tenant_session|auth_|external_identit|login_|refresh_token|global_email_directory)/],
  ['Tenancy & Subscription', /^(tenants?|tenant_|subscription|subscriptions_|plan_|module_catalog|module_|billing|invoice|seat_|feature_access|feature_flag)/],
  ['Platform Administration', /^(platform_|dev_|configuration_template|integration_catalog|integration|system_config|payment_gateway|payment_|oauth_|service_key|provider_)/],
  ['Org Structure', /^(departments?|department_|positions?|position_|org_|legal_entit|legal_|company_)/],
  ['Core HR & Employee Lifecycle', /^(employees?|employee_|employment_|onboarding|offboarding|checklist|bulk_|invitations?|invitation_|access_grant|coverage_|management_coverage|reporting_manager|work_mode)/],
  ['Time & Attendance', /^(attendance|clock_|work_session|breaks?|shift|work_area|late_|time_tracking|general_setting)/],
  ['Leave & Time-Off', /^(leave|entitlement|holiday(?!_calendar)|time_off|balance_)/],
  ['Calendar', /^(calendar|personal_calendar|holiday_calendar|recurrence)/],
  ['Work Management', /^(projects?|project_|objectives?|objective_|sprints?|sprint_|tasks?|task_|work_task|boards?|board_|milestones?|milestone_|labels?|label_|roadmaps?|roadmap_|versions?|version_|release_calendar|allocation|okr)/],
  ['Monitoring & Activity', /^(monitoring|activity_|tray_|device_|inactivity|app_usage|screenshot|meeting|biometric|desktop_agent|productivity|agent_|presence_|exceptions?|discrepanc)/],
  ['Notifications & Outbox', /^(notifications?|notification_|outbox|inbox)/],
  ['Files & Infrastructure', /^(file|blob|storage|entity_asset|asset|idempotency|__ef|audit_log|correlation)/],
  ['Reference Data', /(_status(es)?$|^severit|_severit|_reasons?$|_kinds?$|lookup)/],
];
const DOMAIN_ORDER = [...DOMAINS.map(([n]) => n), 'Other'];

// Expected counts - SPEC.md section 8. A mismatch is a warning, not a failure.
const EXPECTED = {
  'Auth & Identity': 18, 'Tenancy & Subscription': 19, 'Platform Administration': 17,
  'Org Structure': 9, 'Core HR & Employee Lifecycle': 22, 'Time & Attendance': 6,
  'Leave & Time-Off': 13, 'Calendar': 6, 'Work Management': 21, 'Monitoring & Activity': 21,
  'Notifications & Outbox': 3, 'Files & Infrastructure': 6, 'Reference Data': 2,
};
const EXPECTED_TOTAL = 163;

function bucketFor(table) {
  const t = table.toLowerCase();
  for (const [name, re] of DOMAINS) if (re.test(t)) return name;
  return 'Other';
}

// --- env + psql (mirrors ops/postgres/setup-local-db.ps1) -----------------------------------
function readEnv() {
  const env = {};
  const path = join(REPO_ROOT, '.env');
  if (existsSync(path)) {
    for (const raw of readFileSync(path, 'utf8').split(/\r?\n/)) {
      const line = raw.trim();
      if (!line || line.startsWith('#')) continue;
      const i = line.indexOf('=');
      if (i <= 0) continue;
      let v = line.slice(i + 1).trim();
      if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) v = v.slice(1, -1);
      env[line.slice(0, i).trim()] = v;
    }
  }
  return env;
}
function findPsql() {
  const candidates = ['psql'];
  if (process.platform === 'win32') {
    const pf = process.env['ProgramFiles'] || 'C:\\Program Files';
    for (const v of ['18', '17', '16', '15', '14', '13']) candidates.push(join(pf, 'PostgreSQL', v, 'bin', 'psql.exe'));
  } else {
    candidates.push('/usr/bin/psql', '/usr/local/bin/psql', '/opt/homebrew/bin/psql');
  }
  for (const c of candidates) {
    try { execFileSync(c, ['--version'], { stdio: 'ignore' }); return c; } catch { /* keep looking */ }
  }
  throw new Error('psql not found on PATH or in a standard PostgreSQL install directory.');
}
function loadRaw() {
  const env = { ...readEnv(), ...process.env };
  const host = env.ONEVO_DB_HOST || 'localhost';
  const port = env.ONEVO_DB_PORT || '5432';
  const db = env.ONEVO_DB_NAME || 'OnevoDb';
  const user = env.ONEVO_DB_ADMIN_USER || 'postgres';
  const pass = env.ONEVO_DB_ADMIN_PASSWORD || '';
  process.stderr.write(`Reading schema from ${user}@${host}:${port}/${db} ...\n`);
  const out = execFileSync(
    findPsql(),
    ['--no-psqlrc', '-tAX', '-v', 'ON_ERROR_STOP=1', '-h', host, '-p', port, '-U', user, '-d', db, '-f', SQL_FILE],
    { env: { ...process.env, PGPASSWORD: pass }, maxBuffer: 128 * 1024 * 1024, encoding: 'utf8' },
  );
  const s = out.indexOf('{'), e = out.lastIndexOf('}');
  if (s < 0 || e < s) throw new Error(`Unexpected psql output:\n${out.slice(0, 500)}`);
  return JSON.parse(out.slice(s, e + 1));
}

// --- SPEC.md section 5: Postgres type -> Mermaid-safe label --------------------------------------
function simplifyType(c) {
  const dt = c.data_type;
  if (dt === 'character varying') return c.character_maximum_length ? `varchar${c.character_maximum_length}` : 'varchar';
  if (dt === 'character') return c.character_maximum_length ? `char${c.character_maximum_length}` : 'char';
  if (dt === 'timestamp with time zone') return 'timestamptz';
  if (dt === 'timestamp without time zone') return 'timestamp';
  if (dt === 'time with time zone') return 'timetz';
  if (dt === 'time without time zone') return 'time';
  if (dt === 'numeric' && c.numeric_precision != null) return `numeric${c.numeric_precision}_${c.numeric_scale ?? 0}`;
  if (dt === 'USER-DEFINED') return c.udt_name;
  if (dt === 'ARRAY') return `${String(c.udt_name).replace(/^_/, '')}[]`;
  return dt;
}
function keyLabel(c) {
  const k = [];
  if (c.is_pk) k.push('PK');
  if (c.is_fk) k.push('FK');
  if (c.is_uk && !c.is_pk) k.push('UK');
  return k.join(', ');
}
function defaultLabel(d) {
  if (d == null) return '\u2013';
  const s = String(d);
  return s.length > 20 ? `${s.slice(0, 20)}\u2026` : s;
}

// --- model -----------------------------------------------------------------------------------
function build(raw) {
  const columnsByTable = new Map();
  for (const col of raw.columns) {
    if (!columnsByTable.has(col.table_name)) columnsByTable.set(col.table_name, []);
    columnsByTable.get(col.table_name).push(col);
  }
  const allTables = [...columnsByTable.keys()].sort((a, b) => a.localeCompare(b));
  const domainOf = new Map(allTables.map((t) => [t, bucketFor(t)]));
  const nullableOf = new Map();
  for (const [t, cols] of columnsByTable) for (const c of cols) nullableOf.set(`${t}.${c.column_name}`, !!c.is_nullable);

  // collapse composite FKs to one relationship (SPEC.md section 2/7)
  const fkByConstraint = new Map();
  for (const fk of raw.constraints) {
    if (!fkByConstraint.has(fk.constraint_name)) {
      fkByConstraint.set(fk.constraint_name, {
        name: fk.constraint_name, from_table: fk.table_name, to_table: fk.ref_table,
        cols: [], on_delete: fk.delete_rule,
      });
    }
    fkByConstraint.get(fk.constraint_name).cols.push({ from: fk.column_name, to: fk.ref_column });
  }
  const rels = [...fkByConstraint.values()].map((r) => ({
    ...r,
    label: r.cols.map((c) => c.from).join(', '),
    nullable: r.cols.every((c) => nullableOf.get(`${r.from_table}.${c.from}`) === true),
  }));

  const domains = [];
  for (const domain of DOMAIN_ORDER) {
    const tables = allTables.filter((t) => domainOf.get(t) === domain);
    if (!tables.length) continue;
    const inDomain = new Set(tables);

    const sameDomain = rels.filter((r) => inDomain.has(r.from_table) && inDomain.has(r.to_table));

    const tableModels = tables.map((name) => {
      const refsInto = rels
        .filter((r) => r.from_table === name && !inDomain.has(r.to_table) && r.to_table)
        .map((r) => ({ source: `${r.from_table}.${r.label}`, target: `${r.to_table}.${r.cols.map((c) => c.to).join(', ')}`, domain: domainOf.get(r.to_table) || 'Other', on_delete: r.on_delete }));
      const refsBy = rels
        .filter((r) => r.to_table === name && !inDomain.has(r.from_table))
        .map((r) => ({ source: `${r.from_table}.${r.label}`, target: `${name}.${r.cols.map((c) => c.to).join(', ')}`, domain: domainOf.get(r.from_table) || 'Other', on_delete: r.on_delete }));
      return { name, columns: columnsByTable.get(name), refsInto, refsBy };
    });

    domains.push({ domain, tables: tableModels, sameDomain });
  }

  return { meta: raw.meta, domains, domainOf, tableCount: allTables.length };
}

// --- Mermaid (relationship-only; SPEC.md section 7 / section 11) -------------------------------------
function mermaidFor(d) {
  const lines = ['erDiagram'];
  for (const t of d.tables) lines.push(`  ${t.name} {}`);
  for (const r of d.sameDomain) {
    const parentCard = r.nullable ? '|o' : '||';
    lines.push(`  ${r.to_table} ${parentCard}--o{ ${r.from_table} : "${r.label}"`);
  }
  return lines.join('\n');
}

// --- Markdown per domain (SPEC.md section 3, 6, 7, 11) ------------------------------------------
function xtable(rows, kind) {
  if (!rows.length) return '_(none)_\n';
  const domCol = kind === 'into' ? 'Target Domain' : 'Source Domain';
  const head = `| Source | Target | ${domCol} | On Delete |\n|---|---|---|---|\n`;
  return head + rows.map((r) => `| \`${r.source}\` | \`${r.target}\` | ${r.domain} | ${r.on_delete} |`).join('\n') + '\n';
}
function domainMarkdown(d, meta) {
  const out = [];
  out.push('```');
  out.push(`DB: ${meta.database}`);
  out.push(`Last migration: ${meta.last_migration || 'n/a'}`);
  out.push(`Snapshot generated: ${meta.generated_at}`);
  out.push('```', '');
  out.push(`# Domain: ${d.domain}`, '');
  out.push('### ER Diagram (same-domain relationships only)', '');
  out.push('```mermaid');
  out.push(mermaidFor(d));
  out.push('```', '');
  for (const t of d.tables) {
    out.push(`### ${t.name}`, '');
    out.push('| Column | Type | Key | Nullable | Default |');
    out.push('|---|---|---|---|---|');
    for (const c of t.columns) {
      out.push(`| ${c.column_name} | ${simplifyType(c)} | ${keyLabel(c) || ''} | ${c.is_nullable ? 'NULL' : 'NOT NULL'} | ${defaultLabel(c.column_default)} |`);
    }
    out.push('');
    out.push('#### References into other domains', '');
    out.push(xtable(t.refsInto, 'into'));
    out.push('#### Referenced by other domains', '');
    out.push(xtable(t.refsBy, 'by'));
  }
  return out.join('\n');
}

// --- Viewer HTML (SPEC.md section 12 - follows schema-snapshots/viewer-mockup.html) ------------------
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const slug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');

function xtableHtml(rows, kind) {
  if (!rows.length) return '<p class="none">none</p>';
  const domHead = kind === 'into' ? 'Target Domain' : 'Source Domain';
  return `<table><thead><tr><th>Source</th><th>Target</th><th>${domHead}</th><th>On Delete</th></tr></thead><tbody>` +
    rows.map((r) => `<tr><td>${esc(r.source)}</td><td>&rarr; ${esc(r.target)}</td><td>${esc(r.domain)}</td><td>${esc(r.on_delete)}</td></tr>`).join('') +
    '</tbody></table>';
}
function tableCardHtml(t, i) {
  const cols = t.columns.map((c) => {
    const key = keyLabel(c).split(', ').filter(Boolean).map((k) => `<span class="badge">${k}</span>`).join('');
    const nn = c.is_nullable ? 'NULL' : 'NOT NULL';
    return `<tr class="${c.is_nullable ? 'nullable' : ''}"><td>${esc(c.column_name)}</td><td>${esc(simplifyType(c))}</td><td>${key}</td><td>${nn}</td><td>${esc(defaultLabel(c.column_default))}</td></tr>`;
  }).join('');
  return `<details class="table-card"${i === 0 ? ' open' : ''}>
    <summary><span>${esc(t.name)}</span><span class="chevron">&rsaquo;</span></summary>
    <div class="table-card-body">
      <table class="cols"><thead><tr><th>Column</th><th>Type</th><th>Key</th><th>Nullable</th><th>Default</th></tr></thead><tbody>${cols}</tbody></table>
      <div class="xdomain">
        <h4>References into other domains</h4>${xtableHtml(t.refsInto, 'into')}
        <h4>Referenced by other domains</h4>${xtableHtml(t.refsBy, 'by')}
      </div>
    </div>
  </details>`;
}
function domainPanelHtml(d, active) {
  const id = slug(d.domain);
  return `<section class="domain-panel" data-domain="${esc(d.domain)}" id="panel-${id}"${active ? '' : ' hidden'}>
    <div class="domain-heading"><h1>${esc(d.domain)}</h1><span class="table-count">${d.tables.length} tables</span></div>
    <div class="legend">
      <span><b>PK</b> primary key</span><span><b>FK</b> foreign key</span><span><b>UK</b> unique constraint</span>
      <span>NOT NULL columns full opacity, nullable columns muted</span>
    </div>
    <div class="diagram-card"><h2>Same-domain relationships</h2>
      <div class="mermaid">${esc(mermaidFor(d))}</div></div>
    ${d.tables.map(tableCardHtml).join('\n')}
  </section>`;
}

function viewerHtml(model) {
  const { meta, domains } = model;
  const present = new Set(domains.map((d) => d.domain));
  const navDomains = DOMAIN_ORDER.filter((n) => present.has(n) || EXPECTED[n]);
  const counts = Object.fromEntries(domains.map((d) => [d.domain, d.tables.length]));
  const firstId = slug(domains[0].domain);

  const nav = navDomains.map((n) => {
    const id = slug(n);
    const c = counts[n] ?? EXPECTED[n] ?? 0;
    return `<button class="domain-item" data-target="${id}" aria-current="${id === firstId ? 'true' : 'false'}">
      <span class="name"><span class="dot"></span>${esc(n)}</span><span class="count">${c}</span></button>`;
  }).join('\n');

  const panels = navDomains.map((n) => {
    const d = domains.find((x) => x.domain === n);
    if (d) return domainPanelHtml(d, slug(n) === firstId);
    const id = slug(n);
    return `<section class="domain-panel" data-domain="${esc(n)}" id="panel-${id}" hidden>
      <div class="domain-heading"><h1>${esc(n)}</h1><span class="table-count">${EXPECTED[n] ?? 0} tables</span></div>
      <div class="empty-state">Not generated in this snapshot. Rerun
        <code>node schema-snapshots/generate.mjs</code> against a database that has these tables.</div>
    </section>`;
  }).join('\n');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Schema Snapshot &mdash; ${esc(meta.database)} @ ${esc(meta.last_migration || 'n/a')}</title>
<script src="https://cdnjs.cloudflare.com/ajax/libs/mermaid/10.9.0/mermaid.min.js"></script>
<style>
  :root{
    --ink:#1B2430; --ink-soft:#2C3644; --paper:#FBFAF7; --paper-line:#DED8CB;
    --text:#1B2430; --text-muted:#68707D; --accent:#B9832E; --accent-soft:#EFE1C6;
    --line:#4A6670; --radius:3px;
    --serif:'Iowan Old Style','Palatino Linotype','Book Antiqua',Georgia,serif;
    --mono:'JetBrains Mono','Fira Code',ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;
  }
  *{box-sizing:border-box;} html,body{margin:0;padding:0;}
  body{background:var(--paper);color:var(--text);font-family:var(--serif);font-size:16px;line-height:1.5;}
  a{color:var(--ink);}
  .app{display:grid;grid-template-columns:280px 1fr;min-height:100vh;}
  .sidebar{background:var(--ink);color:#E9E6DD;padding:24px 20px 20px;display:flex;flex-direction:column;gap:18px;position:sticky;top:0;height:100vh;overflow:hidden;}
  .snap-meta{font-family:var(--mono);font-size:12.5px;line-height:1.7;color:#B8C0CC;border-bottom:1px solid #3A4657;padding-bottom:16px;}
  .snap-meta .db-name{display:block;font-family:var(--serif);font-size:19px;color:#fff;margin-bottom:6px;}
  .search-wrap input{width:100%;background:var(--ink-soft);border:1px solid #3A4657;color:#fff;padding:9px 12px;border-radius:var(--radius);font-family:var(--serif);font-size:14px;}
  .search-wrap input::placeholder{color:#8892A0;}
  .search-wrap input:focus-visible{outline:2px solid var(--accent);outline-offset:1px;}
  nav.domain-list{display:flex;flex-direction:column;gap:2px;overflow-y:auto;}
  .domain-item{display:flex;align-items:center;justify-content:space-between;gap:8px;width:100%;background:transparent;border:none;color:#D6D9DE;text-align:left;padding:9px 10px;border-radius:var(--radius);font-family:var(--serif);font-size:14.5px;cursor:pointer;}
  .domain-item:hover{background:var(--ink-soft);}
  .domain-item:focus-visible{outline:2px solid var(--accent);outline-offset:-2px;}
  .domain-item .count{font-family:var(--mono);font-size:12px;color:#8892A0;}
  .domain-item[aria-current="true"]{background:var(--ink-soft);color:#fff;}
  .domain-item[aria-current="true"] .dot{display:inline-block;width:6px;height:6px;border-radius:50%;background:var(--accent);margin-right:8px;}
  .domain-item .dot{display:none;}
  .domain-item .name{display:flex;align-items:center;}
  main.content{padding:36px 48px 80px;max-width:900px;}
  .domain-heading{display:flex;align-items:baseline;gap:12px;margin-bottom:4px;}
  .domain-heading h1{font-size:32px;margin:0;font-weight:600;}
  .domain-heading .table-count{font-family:var(--mono);font-size:13px;color:var(--text-muted);}
  .legend{display:flex;gap:18px;flex-wrap:wrap;margin:18px 0 28px;padding:12px 14px;border:1px solid var(--paper-line);border-radius:var(--radius);font-family:var(--mono);font-size:12px;color:var(--text-muted);}
  .legend span b{color:var(--accent);font-weight:600;}
  .diagram-card{border:1px solid var(--paper-line);border-radius:var(--radius);padding:18px 20px 6px;margin-bottom:32px;background:#fff;overflow:auto;max-height:80vh;resize:vertical;}
  .diagram-card h2{font-size:13px;color:var(--text-muted);margin:0 0 8px;font-family:var(--serif);font-weight:600;}
  .diagram-card .mermaid svg{max-width:none!important;height:auto;}
  details.table-card{border:1px solid var(--paper-line);border-radius:var(--radius);margin-bottom:14px;background:#fff;overflow:hidden;}
  details.table-card > summary{list-style:none;cursor:pointer;padding:14px 18px;font-family:var(--mono);font-size:15px;font-weight:600;display:flex;align-items:center;justify-content:space-between;}
  details.table-card > summary::-webkit-details-marker{display:none;}
  details.table-card > summary:focus-visible{outline:2px solid var(--accent);outline-offset:-2px;}
  details.table-card > summary .chevron{font-family:var(--serif);color:var(--text-muted);transition:transform .15s ease;}
  details[open] > summary .chevron{transform:rotate(90deg);}
  .table-card-body{padding:0 18px 18px;border-top:1px solid var(--paper-line);}
  table.cols{width:100%;border-collapse:collapse;font-family:var(--mono);font-size:13px;margin-top:12px;}
  table.cols th{text-align:left;font-family:var(--serif);font-weight:600;font-size:12.5px;color:var(--text-muted);padding:6px 10px 6px 0;border-bottom:1px solid var(--paper-line);}
  table.cols td{padding:7px 10px 7px 0;border-bottom:1px solid #EEEAE0;}
  table.cols tr.nullable td{color:var(--text-muted);}
  .badge{display:inline-block;padding:1px 6px;border-radius:2px;font-size:11px;background:var(--accent-soft);color:#7A5A17;margin-right:4px;}
  .xdomain{margin-top:16px;}
  .xdomain h4{font-family:var(--serif);font-size:13px;font-weight:600;color:var(--text-muted);margin:14px 0 6px;}
  .xdomain table{width:100%;border-collapse:collapse;font-family:var(--mono);font-size:12.5px;}
  .xdomain th{text-align:left;font-family:var(--serif);font-weight:600;font-size:11.5px;color:var(--text-muted);padding:4px 8px 4px 0;border-bottom:1px solid var(--paper-line);}
  .xdomain td{padding:6px 8px 6px 0;border-bottom:1px solid #EEEAE0;}
  .xdomain .none{font-family:var(--serif);font-style:italic;color:var(--text-muted);font-size:13px;}
  .empty-state{border:1px dashed var(--paper-line);border-radius:var(--radius);padding:28px 24px;color:var(--text-muted);font-size:15px;max-width:60ch;}
  .empty-state code{font-family:var(--mono);font-size:13px;background:#F1EEE5;padding:1px 5px;border-radius:2px;}
  [hidden]{display:none!important;}
  @media (max-width:760px){
    .app{grid-template-columns:1fr;}
    .sidebar{position:static;height:auto;flex-direction:column;padding:16px;}
    nav.domain-list{flex-direction:row;flex-wrap:wrap;overflow-x:auto;}
    main.content{padding:24px 20px 60px;}
  }
</style>
</head>
<body>
<div class="app">
  <aside class="sidebar">
    <div class="snap-meta">
      <span class="db-name">${esc(meta.database)}</span>
      migration: ${esc(meta.last_migration || 'n/a')}<br>
      snapshot: ${esc(meta.generated_at)}
    </div>
    <div class="search-wrap">
      <input type="text" id="domainSearch" placeholder="Filter domains&hellip;" aria-label="Filter domains">
    </div>
    <nav class="domain-list" id="domainList" aria-label="Domains">
      ${nav}
    </nav>
  </aside>
  <main class="content">
    ${panels}
  </main>
</div>
<script>
  mermaid.initialize({ startOnLoad:false, theme:'neutral', er:{ useMaxWidth:false }, maxTextSize:500000, maxEdges:5000 });
  const drawn = new WeakSet();
  function draw(panel){
    const el = panel && panel.querySelector('.mermaid');
    if (!el || drawn.has(el)) return;
    drawn.add(el);
    try { mermaid.run({ nodes:[el] }); }
    catch (e) { el.insertAdjacentHTML('afterend', '<p style="color:#c00;font-size:12px">Mermaid failed: ' + (e && e.message || e) + '</p>'); }
  }
  function show(id){
    document.querySelectorAll('.domain-panel').forEach(p => p.hidden = (p.id !== 'panel-' + id));
    document.querySelectorAll('.domain-item').forEach(b => b.setAttribute('aria-current', b.dataset.target === id ? 'true' : 'false'));
    draw(document.getElementById('panel-' + id));
    history.replaceState(null, '', '#' + id);
  }
  document.getElementById('domainList').addEventListener('click', e => {
    const b = e.target.closest('.domain-item'); if (b) show(b.dataset.target);
  });
  document.getElementById('domainSearch').addEventListener('input', e => {
    const q = e.target.value.trim().toLowerCase();
    document.querySelectorAll('.domain-item').forEach(b => {
      b.style.display = b.querySelector('.name').textContent.toLowerCase().includes(q) ? '' : 'none';
    });
  });
  const start = location.hash.replace('#', '') || document.querySelector('.domain-item')?.dataset.target;
  if (start) show(start);
</script>
</body>
</html>
`;
}

// --- write everything ----------------------------------------------------------------------------
function writeAll(model, raw) {
  const { meta } = model;
  const stamp = `${meta.last_migration || 'unknown'}-${meta.generated_at.slice(0, 10)}`;
  const rawDir = join(HERE, 'raw', meta.last_migration || 'unknown');
  const outDir = join(HERE, stamp);
  mkdirSync(rawDir, { recursive: true });
  mkdirSync(outDir, { recursive: true });

  writeFileSync(join(rawDir, 'meta.json'), JSON.stringify(raw.meta, null, 2));
  writeFileSync(join(rawDir, 'columns.json'), JSON.stringify(raw.columns, null, 2));
  writeFileSync(join(rawDir, 'constraints.json'), JSON.stringify(raw.constraints, null, 2));

  for (const d of model.domains) {
    writeFileSync(join(outDir, `${slug(d.domain)}.md`), domainMarkdown(d, meta));
  }
  writeFileSync(join(outDir, 'viewer.html'), viewerHtml(model));

  // sanity - SPEC.md section 8
  const lines = [`Snapshot ${stamp}`, `  raw/    -> ${rawDir}`, `  output/ -> ${outDir}`, ''];
  let total = 0;
  for (const d of model.domains) {
    total += d.tables.length;
    const exp = EXPECTED[d.domain];
    const flag = exp != null && exp !== d.tables.length ? `  <-- SPEC.md expects ${exp}` : '';
    lines.push(`  ${String(d.tables.length).padStart(3)}  ${d.domain}${flag}`);
  }
  lines.push('  ---');
  lines.push(`  ${String(total).padStart(3)}  total${total !== EXPECTED_TOTAL ? `  <-- SPEC.md expects ${EXPECTED_TOTAL}` : ''}`);
  process.stderr.write(lines.join('\n') + '\n');
}

try {
  const raw = loadRaw();
  const model = build(raw);
  writeAll(model, raw);
} catch (err) {
  process.stderr.write(`\nSchema snapshot generation failed: ${err.message}\n`);
  process.exit(1);
}
