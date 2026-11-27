/*
Task 18: the top queries from Query Store, for the index review.

Run against the database the baseline ran on (a throwaway one locally, or UAT with Query Store already on):

  sqlcmd -S <server> -d <db> -C -E -i ops/perf-baseline/query-store-top.sql -v TOP=30 -W -s "|" -o top-queries.txt

Two lists: by total duration and by total logical reads, both over everything Query Store holds. Each row has the
query id, executions, average and total duration (ms), average logical reads, average rows and the query text. The
plan is read separately with:

  SELECT CAST(query_plan AS xml) FROM sys.query_store_plan WHERE plan_id = <plan id>;

To start from a clean store before a run (only on a database you own):

  ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);
  ALTER DATABASE CURRENT SET QUERY_STORE CLEAR;
*/
SET NOCOUNT ON;

WITH stats AS (
    SELECT p.query_id,
           MAX(p.plan_id) AS plan_id,
           SUM(rs.count_executions) AS executions,
           SUM(rs.avg_duration * rs.count_executions) / 1000.0 AS total_ms,
           SUM(rs.avg_logical_io_reads * rs.count_executions) AS total_reads,
           SUM(rs.avg_rowcount * rs.count_executions) AS total_rows
    FROM sys.query_store_runtime_stats rs
    JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id
    GROUP BY p.query_id
),
ranked AS (
    SELECT s.*,
           ROW_NUMBER() OVER (ORDER BY s.total_ms DESC) AS by_duration,
           ROW_NUMBER() OVER (ORDER BY s.total_reads DESC) AS by_reads
    FROM stats s
)
SELECT r.by_duration,
       r.by_reads,
       r.query_id,
       r.plan_id,
       r.executions,
       CAST(r.total_ms / NULLIF(r.executions, 0) AS decimal(12, 2)) AS avg_ms,
       CAST(r.total_ms AS decimal(14, 1)) AS total_ms,
       CAST(r.total_reads / NULLIF(r.executions, 0) AS bigint) AS avg_reads,
       CAST(r.total_rows / NULLIF(r.executions, 0) AS bigint) AS avg_rows,
       REPLACE(REPLACE(LEFT(t.query_sql_text, 4000), CHAR(13), ' '), CHAR(10), ' ') AS query_text
FROM ranked r
JOIN sys.query_store_query q ON q.query_id = r.query_id
JOIN sys.query_store_query_text t ON t.query_text_id = q.query_text_id
WHERE r.by_duration <= $(TOP) OR r.by_reads <= $(TOP)
ORDER BY r.by_duration;
