# Task 11: Secrets out of the repository and scans in CI

| | |
|---|---|
| Priority | 11 of 30 (lower number = build first) |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security, Deployment |
| Depends on | nothing |

## What we found

- `src/ONEVO.Api/appsettings.Development.json` is tracked in git and contains a real value for `TrayApp:Jwt:Secret`.
- The same file contains a personal email address and full name under `PlatformBootstrap`.
- `ci.yml` runs no secret scan, no dependency scan and no static analysis. The scanners exist only as local scripts in `dev-env`.
- There is no Dependabot or Renovate configuration under `.github`.

## What to do

1. Move the tray JWT secret and the bootstrap admin details to `.env` (already git-ignored) and leave empty placeholders in the settings file. Update `.env.example`.
2. Treat the committed secret as leaked: generate a new one for every environment. Make the start-up validator reject the old value outside Development.
3. Add a CI job that runs gitleaks on every pull request and fails on a new finding. Reuse the baseline in `dev-env/baselines`.
4. Add `dotnet list package --vulnerable --include-transitive` to CI and fail on High or Critical.
5. Add Dependabot for NuGet and GitHub Actions, weekly.
6. Pin GitHub Actions to commit hashes instead of tags.
7. Ask the repository owner before changing CI, because it affects every developer.

## Done when

- No secret value remains in tracked files, and git history cleaning has been discussed and decided.
- A pull request that adds a fake secret fails CI.
- The vulnerable package check runs on every pull request.

## Status (2026-11-23)

On hold (confirmed 2026-11-23, no permission yet). It needs the repository owner's agreement for the CI changes and the git history decision (step 7) before work starts.
