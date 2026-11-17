# Task 25: Split service registration by module

| | |
|---|---|
| Priority | 25 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Architecture, Code quality |
| Depends on | nothing |

## What we found

- `Infrastructure/DependencyInjection.cs` is 1,149 lines. Every repository, service, job, HTTP client and seeder for every module is registered in one method. Two people adding a feature in different modules edit the same file, which causes merge conflicts.
- `Program.cs` registers hub and presence services by hand with full type names.
- Other large files that mix several jobs: `EfEmployeeRepository.cs` (1,037 lines), `OnboardingDraftWriteService.cs` (903), `AttendanceReadHandlers.cs` (748, many handlers in one file), `EmployeesController.cs` (692).

## What to do

1. Split registration into one extension method per module (`AddAuthInfrastructure`, `AddWorkManagementInfrastructure`, `AddLeaveInfrastructure` and so on), each in its module folder. `AddInfrastructure` only calls them in order.
2. Keep the registration order for anything where order matters (`Replace` calls, interceptors, hosted services). Take a list of registered services before and after and compare them to prove nothing changed.
3. Move the hub and presence registrations out of `Program.cs` into an `AddApiRealtime` extension.
4. Write a short `docs/development/architecture-rules.md`: where a new repository, service, job and validator go, and the size at which a file should be split.
5. Do not split the large classes in this task. List them with a suggested split as follow-up tasks.

## Done when

- `DependencyInjection.cs` is under 100 lines.
- The before and after service lists are identical.
- All three test suites pass.
