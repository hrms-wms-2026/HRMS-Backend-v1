# Unachieve Project

**POST** `/api/v1/work/projects/{id}/unachieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the Project's Lead (`leadId` compared as the caller's Employee id as of 2026-08-14).

## Description

Reverts an Achieved project back to active. Lead-only, always immediate.

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or is not the project lead |
| `404` | Project doesn't exist in tenant |
| `409` | Project is not achieved |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Unachieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/UnachieveProject/UnachieveProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
