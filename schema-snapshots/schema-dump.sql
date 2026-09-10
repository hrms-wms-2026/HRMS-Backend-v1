-- Raw extraction for the schema snapshot (SPEC.md section 4).
-- Emits ONE JSON object: { meta, columns[], constraints[] }.
-- generate.mjs runs this, saves the pieces under raw/<migration_id>/, then builds
-- the per-domain Markdown + viewer.
--
--   psql "<conn>" -tAX -f schema-snapshots/schema-dump.sql
--
-- Scope: schema "public", base tables only. Foreign keys come from pg_catalog so
-- composite keys are correct; generate.mjs collapses a composite FK to a single
-- relationship (SPEC.md section 2 / section 7).

WITH cols AS (
  SELECT
    c.table_name,
    c.ordinal_position,
    c.column_name,
    c.data_type,
    c.udt_name,
    c.character_maximum_length,
    c.numeric_precision,
    c.numeric_scale,
    (c.is_nullable = 'YES') AS is_nullable,
    c.column_default
  FROM information_schema.columns c
  JOIN information_schema.tables t
    ON t.table_schema = c.table_schema AND t.table_name = c.table_name
  WHERE t.table_schema = 'public' AND t.table_type = 'BASE TABLE'
),
pk AS (
  SELECT cl.relname AS table_name, att.attname AS column_name
  FROM pg_constraint con
  JOIN pg_class cl     ON cl.oid = con.conrelid
  JOIN pg_namespace ns ON ns.oid = cl.relnamespace AND ns.nspname = 'public'
  JOIN unnest(con.conkey) AS k(attnum) ON TRUE
  JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.attnum
  WHERE con.contype = 'p'
),
uq AS (
  SELECT cl.relname AS table_name, att.attname AS column_name,
         con.conname AS constraint_name, array_length(con.conkey, 1) AS width
  FROM pg_constraint con
  JOIN pg_class cl     ON cl.oid = con.conrelid
  JOIN pg_namespace ns ON ns.oid = cl.relnamespace AND ns.nspname = 'public'
  JOIN unnest(con.conkey) AS k(attnum) ON TRUE
  JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.attnum
  WHERE con.contype = 'u'
),
fk AS (
  SELECT
    con.conname AS constraint_name,
    cl.relname  AS table_name,
    att.attname AS column_name,
    k.ord       AS col_ord,
    fcl.relname AS ref_table,
    fatt.attname AS ref_column,
    CASE con.confdeltype
      WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE'
      WHEN 'n' THEN 'SET NULL'  WHEN 'd' THEN 'SET DEFAULT' ELSE con.confdeltype::text
    END AS delete_rule
  FROM pg_constraint con
  JOIN pg_class cl        ON cl.oid = con.conrelid
  JOIN pg_namespace ns    ON ns.oid = cl.relnamespace AND ns.nspname = 'public'
  JOIN pg_class fcl       ON fcl.oid = con.confrelid
  JOIN unnest(con.conkey)  WITH ORDINALITY AS k(attnum, ord) ON TRUE
  JOIN unnest(con.confkey) WITH ORDINALITY AS f(attnum, ord) ON f.ord = k.ord
  JOIN pg_attribute att   ON att.attrelid = con.conrelid  AND att.attnum = k.attnum
  JOIN pg_attribute fatt  ON fatt.attrelid = con.confrelid AND fatt.attnum = f.attnum
  WHERE con.contype = 'f'
)
SELECT json_build_object(
  'meta', json_build_object(
    'database', current_database(),
    'last_migration', (SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1),
    'generated_at', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
    'base_table_count', (SELECT count(*) FROM information_schema.tables
                         WHERE table_schema = 'public' AND table_type = 'BASE TABLE')
  ),
  'columns', (
    SELECT COALESCE(json_agg(json_build_object(
      'table_name', table_name,
      'ordinal_position', ordinal_position,
      'column_name', column_name,
      'data_type', data_type,
      'udt_name', udt_name,
      'character_maximum_length', character_maximum_length,
      'numeric_precision', numeric_precision,
      'numeric_scale', numeric_scale,
      'is_nullable', is_nullable,
      'column_default', column_default,
      'is_pk', EXISTS (SELECT 1 FROM pk WHERE pk.table_name = cols.table_name AND pk.column_name = cols.column_name),
      'is_fk', EXISTS (SELECT 1 FROM fk WHERE fk.table_name = cols.table_name AND fk.column_name = cols.column_name),
      'is_uk', EXISTS (SELECT 1 FROM uq WHERE uq.table_name = cols.table_name AND uq.column_name = cols.column_name)
    ) ORDER BY table_name, ordinal_position), '[]'::json)
    FROM cols
  ),
  'constraints', (
    SELECT COALESCE(json_agg(json_build_object(
      'table_name', table_name,
      'column_name', column_name,
      'col_ord', col_ord,
      'constraint_name', constraint_name,
      'ref_table', ref_table,
      'ref_column', ref_column,
      'delete_rule', delete_rule
    ) ORDER BY table_name, constraint_name, col_ord), '[]'::json)
    FROM fk
  )
);
