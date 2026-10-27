# `development` branch — merge history + CI / integration-test status

Repo: `hrms-wms-2026/HRMS-Backend-v1`  ·  branch: `origin/development`  ·  generated 2026-09-03
Source: `git log --merges --first-parent origin/development` + GitHub Actions API (`.github/workflows/ci.yml`).

## Pipeline model (one workflow: **CI**)

| Job | Trigger | Blocking? | What it runs |
|-----|---------|-----------|--------------|
| `build-and-test` | every PR + every push | **YES** (required status check) | Release build of API + Unit tests + Architecture tests |
| `integration-routing` | pull_request **only** | No (`continue-on-error`) | path-routed subset of integration suite. On a merge/push it always shows **skipped** |
| `full-integration` | push / nightly cron / manual | No (`continue-on-error`) | **full** Testcontainers (real Postgres) integration suite |
| `integration-tests` (old name, before 2026-08-11 / PR #58) | every push | No (`continue-on-error`) | integration suite (pre-split) |

So on a **merge commit to `development`** the relevant integration signal is **`full-integration`** (or `integration-tests` before 2026-08-11). It is **non-blocking** — a red integration job never blocked the merge.

## Key finding

- **`build-and-test` (the gate) has been green on every merge** except:
  - **PR #59** (`e1bbf998`, 2026-08-13) — `build-and-test` = **failure**, merged anyway.
  - **PR #89** (`8ef5db71`, 2026-08-26) — workflow `startup_failure`, jobs never ran.
  - **PR #91** (`ec459e56`, 2026-08-26) — workflow `cancelled`, no jobs.
- **Integration tests (`full-integration`) have been RED on every merge since PR #75 (2026-08-18)** — 22 consecutive merges through PR #104. Known pre-existing CSRF issue in the integration suite (documented in `ci.yml`), non-blocking.
- Integration tests **passed** on merges **PR #58 → PR #70** (2026-08-11 → 2026-08-18).
- Before 2026-08-11 the job was `integration-tests`; spot checks: #42 ✅, #43 ✅, #40 ❌. Full per-merge history for #2–#57 not yet retrieved (GitHub anon API rate limit).

## Merge table

Legend: ✅ success · ❌ failure · ⏭️ skipped · ⏳ running · — job didn't exist · ⚠️ run never completed

| PR | Date | Merge SHA | `build-and-test` | integration (`full-integration` / `integration-tests`) | Workflow result |
|----|------|-----------|------------------|--------------------------------------------------------|-----------------|
| #105 | 2026-09-03 | `d45fdfd5` | ✅ | ⏳ in_progress | running |
| #104 | 2026-09-03 | `612fe4f3` | ✅ | ❌ | success |
| #103 | 2026-09-02 | `333c8404` | ✅ | ❌ | success |
| #101 | 2026-09-02 | `ca0212bd` | ✅ | ❌ | success |
| #100 | 2026-09-02 | `07d51717` | ✅ | ❌ | success |
| #99 | 2026-08-30 | `887bfff0` | ✅ | ❌ | success |
| #98 | 2026-08-28 | `0d2de30a` | ✅ | ❌ | success |
| #97 | 2026-08-27 | `1b90eab2` | ✅ | ❌ | success |
| #96 | 2026-08-27 | `7ccfe96c` | ✅ | ❌ | success |
| #93 | 2026-08-27 | `a4454725` | ✅ | ❌ | success |
| #91 | 2026-08-26 | `ec459e56` | ⚠️ | ⚠️ | cancelled |
| #89 | 2026-08-26 | `8ef5db71` | ⚠️ queued | ⚠️ queued | startup_failure |
| #90 | 2026-08-25 | `46d57d78` | ✅ | ❌ | success |
| #85 | 2026-08-25 | `2cd0cd9f` | ✅ | ❌ | success |
| #88 | 2026-08-24 | `d9561703` | ✅ | ❌ | success |
| #84 | 2026-08-24 | `97478170` | ✅ | ❌ | success |
| #82 | 2026-08-24 | `40826f2c` | ✅ | ❌ | success |
| #83 | 2026-08-23 | `c8766301` | ✅ | ❌ | success |
| #80 | 2026-08-21 | `ed6c48b2` | ✅ | ❌ | success |
| #79 | 2026-08-21 | `1a49b6cc` | ✅ | ❌ | success |
| #77 | 2026-08-21 | `71519142` | ✅ | ❌ | success |
| #76 | 2026-08-21 | `fb0f66a2` | ✅ | ❌ | success |
| #81 | 2026-08-20 | `010e3c3f` | ✅ | ❌ | success |
| #78 | 2026-08-19 | `4c98978f` | ✅ | ❌ | success |
| #75 | 2026-08-18 | `62387da8` | ✅ | ❌ (first red) | success |
| #70 | 2026-08-18 | `971cbd60` | ✅ | ✅ | success |
| #69 | 2026-08-18 | `de6011da` | ✅ | ✅ | success |
| #68 | 2026-08-18 | `5a3c1b54` | ✅ | ✅ | success |
| #67 | 2026-08-17 | `be3fbfcc` | ✅ | ✅ | success |
| #66 | 2026-08-17 | `a9a54802` | ✅ | ✅ | success |
| #62 | 2026-08-15 | `d2465eaa` | ✅ | ✅ | success |
| #63 | 2026-08-15 | `a6a15e2f` | ✅ | ✅ | success |
| #64 | 2026-08-15 | `7d6c0fa1` | ✅ | ✅ | success |
| #59 | 2026-08-13 | `e1bbf998` | ❌ | ✅ | **failure** |
| #61 | 2026-08-13 | `e1c5f7b3` | ✅ | ✅ | success |
| #58 | 2026-08-11 | `51248f61` | ✅ | ✅ | success |
| #48 | 2026-08-10 | `7d1f8631` | ✅ | ✅ (`integration-tests`, spot-check pending) | success |
| #57 | 2026-08-10 | `a6902839` | ✅ | ? `integration-tests` | success |
| #53 | 2026-08-10 | `13cc6199` | ✅ | ? `integration-tests` | success |
| #56 | 2026-08-10 | `37bf4646` | ✅ | ? `integration-tests` | success |
| #52 | 2026-08-10 | `263f2fd1` | ✅ | ? `integration-tests` | success |
| #47 | 2026-08-10 | `ffe48524` | ✅ | ? `integration-tests` | success |
| #46 | 2026-08-10 | `5dc74f0d` | ✅ | ? `integration-tests` | success |
| #51 | 2026-08-09 | `53febc09` | ✅ | ? `integration-tests` | success |
| #43 | 2026-08-07 | `a99bf427` | ✅ | ✅ `integration-tests` | success |
| #42 | 2026-08-07 | `5e0eaf34` | ✅ | ✅ `integration-tests` | success |
| #40 | 2026-08-06 | `e6c7981e` | ✅ | ❌ `integration-tests` | success |
| #38 | 2026-08-06 | `3c8a818b` | ✅ | ? | success |
| #39 | 2026-08-06 | `99b5e0f1` | ✅ | ? | success |
| #37 | 2026-08-05 | `c356464d` | ✅ | ? | success |
| #35 | 2026-08-05 | `78b8e6d1` | ✅ | ? | success |
| #34 | 2026-08-05 | `d7e3c72c` | ✅ | ? | success |
| #32 | 2026-08-05 | `e08b4b9c` | ✅ | ? | success |
| #33 | 2026-08-05 | `a8978374` | ✅ | ? | success |
| #30 | 2026-08-04 | `20d59f17` | ✅ | ? | success |
| #28 | 2026-08-04 | `416a6ec5` | ✅ | ? | success |
| #27 | 2026-08-04 | `44619af0` | ✅ | ? | success |
| #26 | 2026-08-04 | `79f48ca6` | ✅ | ? | success |
| #25 | 2026-08-03 | `50bffe62` | ✅ | ? | success |
| #23 | 2026-07-31 | `6dba25de` | ✅ | ? | success |
| #22 | 2026-07-30 | `94108374` | ✅ | ? | success |
| #20 | 2026-07-29 | `f3e62c17` | ✅ | ? | success |
| #18 | 2026-07-29 | `81b62d81` | ✅ | ? | success |
| #17 | 2026-07-29 | `a20c4b87` | ✅ | ? | success |
| #15 | 2026-07-28 | `ac4f93f5` | ✅ | ? | success |
| #13 | 2026-07-28 | `1851adc0` | ✅ | ? | success |
| #10 | 2026-07-27 | `74064d41` | ✅ | ? | success |
| #9 | 2026-07-27 | `f34e2404` | ✅ | ? | success |
| #6 | 2026-07-26 | `ce15e25c` | ✅ | ? | success |
| #5 | 2026-07-26 | `b4a0d114` | ✅ | ? | success |
| #4 | 2026-07-22 | `cab60b80` | ✅ | ? | success |
| #2 | 2026-07-20 | `030115b0` | — | — | no CI run |

`?` = `build-and-test` / workflow result known from the run summary, but the per-job integration
conclusion for that merge was not fetched (GitHub anonymous API is 60 req/hr and was exhausted).

## To fill the `?` rows

```bash
gh auth login          # one-time; raises the API limit to 5000/hr
```

Then re-run the fetch script in the session scratchpad (`jobs.js`), or:

```bash
gh run list --repo hrms-wms-2026/HRMS-Backend-v1 --branch development --limit 100 --json headSha,conclusion,databaseId,event
```
