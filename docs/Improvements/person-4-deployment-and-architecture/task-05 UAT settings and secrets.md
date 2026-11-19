# Task 05: UAT settings and secrets

| | |
|---|---|
| Priority | 5 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Deployment, Security |
| Depends on | 01 |

## What we found

- `appsettings.json` has `AllowedOrigins` set to `http://localhost:5173` and `https://app.example.com`. These are placeholders.
- `AllowedHosts` is `*`.
- `ForwardedHeaders:KnownProxies` and `KnownNetworks` are empty. Behind a load balancer the client IP and the tenant host come from forwarded headers, so wrong values here break tenant resolution and rate limiting.
- `Tenancy:RootDomain`, all `Urls:*` and `Email:*` values are empty.
- There is no `appsettings.Staging.json` or UAT file. `ConfigurationStartupValidator` validates "required local configuration"; check what it enforces outside Development.

## What to do

1. List every setting UAT needs in `docs/deployment/uat-configuration.md`, with where the value comes from and whether it is a secret.
2. Secrets (database passwords, `Encryption__MasterKey`, JWT secrets, email and storage keys, Stripe keys) come only from the host secret store or environment variables. None go in a settings file.
3. Set `AllowedHosts` to the UAT root domain and its wildcard. Set `AllowedOrigins`, `Tenancy:RootDomain`, `AdminCookieDomain`, `Urls:*`.
4. Fill `ForwardedHeaders` with the real load balancer addresses.
5. Extend the start-up validator so the API refuses to start in a non-Development environment when a required value is empty or still a placeholder (`example.com`, `localhost`).
6. Keep Swagger off in UAT unless it sits behind admin sign-in.

## Done when

- The API starts in a UAT-like environment using only documented settings.
- Starting with a missing or placeholder value fails with a clear message.
- A tenant subdomain resolves correctly through the load balancer.
