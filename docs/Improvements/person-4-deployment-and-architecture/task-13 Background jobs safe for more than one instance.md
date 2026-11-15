# Task 13: Background jobs safe for more than one instance

| | |
|---|---|
| Priority | 13 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Scaling, Architecture |
| Depends on | nothing |

## What we found

`Infrastructure/DependencyInjection.cs` registers about 35 hosted services in the web process: recurring jobs (leave credit, meeting generation and attendance, calendar sync, Teams and Zoom attendance sync, outbox processor, bulk onboarding, monitoring retention purge, exception detection, project monitor, inbox escalation, activity summaries and more) and about 15 seeders that run at start-up.

When UAT or production runs two API instances, every job runs twice unless it takes a lock. `SqlAppLock` exists, but it is not clear that every job uses it. Jobs also compete with user requests for CPU and database connections.

## What to do

1. Make a table of every hosted service: what it does, how often, whether it takes `SqlAppLock`, whether it is safe to run twice.
2. Add the lock to every job that is not safe. Add a test for at least the outbox processor and leave credit job.
3. Add a setting `BackgroundJobs:Enabled`. With it the same image can run as a web instance (jobs off) or a worker instance (jobs on).
4. Move seeders out of normal start-up: reference data seeders run as part of the deploy step from task 09, not on every boot of every instance.
5. Give each job a time limit and a log line with duration and rows processed.

## Done when

- Two instances started together do not double-process any job (shown by a test or a log check).
- The API can start with jobs disabled.
- The job table is saved in `docs/deployment/background-jobs.md`.
