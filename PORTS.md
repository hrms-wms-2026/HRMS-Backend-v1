# ONEXSO Local Dev — Port Assignments

Canonical copy. Also mirrored in `HRMS-Platform-Administration-Front-End-v1/PORTS.md` — keep both
in sync.

| App                | URL                                      | Repo                                                   |
|--------------------|-------------------------------------------|---------------------------------------------------------|
| Backend API        | `https://onexso.com:7229`                 | `HRMS-Backend-v1` (this repo)                            |
| Tenant App         | `https://{tenant}.onexso.com:4200`        | `Hrms--Web-application---front-end---v1`                 |
| Platform Admin     | `https://admin.onexso.com:4300`           | `HRMS-Platform-Administration-Front-End-v1`               |

Only one dev server can bind a given port at a time. The Tenant App and Platform Admin apps must
never share a port — the "wrong app answers on this port" failure mode doesn't look like an
obvious port conflict; it can surface as a certificate mismatch, or as the wrong app's UI simply
loading on that hostname.
