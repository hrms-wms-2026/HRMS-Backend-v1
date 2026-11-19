# Part 3: Raw SQL and changed behaviour

Read first: `part-0-index.md` (Global Constraints), spec section 8, and the spike report for what it found about MERGE. Part 2 is complete: the schema exists, RLS is on, and `SqlServerErrors` is available.

Every test here uses a real SQL Server container and the restricted `onevo_app` login, not the migrator. Tests go in the temporary `tests/ONEVO.Tests.SqlServer` project and use its `SqlServerFixture`; Part 4 Task 1 moves them into `ONEVO.Tests.Integration`.

### Task 1: Concurrency-critical SQL

**Files:**
- Modify: `src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxClaimStore.cs`, `Outbox/OutboxWriter.cs`, `Services/SharedPlatform/Jobs/JobLeaseService.cs`
- Modify: `Persistence/Repositories/WorkManagement/Planner/EfPlannerRepository.cs` (advisory lock), `WorkManagement/EfWorkApprovalRequestRepository.cs` (`FOR UPDATE`), `WorkManagement/EfProjectRepository.cs` (`RETURNING` counters), `Monitoring/TrayActivation/EfTrayActivationRepository.cs`
- Test: `tests/ONEVO.Tests.SqlServer/ConcurrencyBehaviourTests.cs`

- [ ] **Step 1: Write the tests**

- `OutboxClaim_TwoWorkers_NeverClaimSameRow`: 2 workers, 1,000 rows, each row claimed exactly once.
- `OutboxWrite_DuplicateKey_IsIgnored`.
- `JobLease_OnlyOneHolder`: 8 concurrent acquirers, exactly one wins; expired lease can be taken.
- `ProjectCounter_ConcurrentIncrements_AreUnique`: 50 concurrent, 50 distinct numbers.
- `PlannerLock_SerialisesSameKey`.
- `ApprovalRow_SecondLockerWaits`.

- [ ] **Step 2: Confirm they fail on the Postgres SQL, then rewrite** per the spec 8 table

- Claim: `UPDATE TOP (@n) ... WITH (UPDLOCK, READPAST, ROWLOCK) ... OUTPUT inserted.*`.
- `sp_getapplock` with `@LockOwner = 'Transaction'` in place of `pg_advisory_xact_lock`. Check the return code: below 0 means the lock was not granted, and that must throw.
- Counters: `UPDATE ... OUTPUT inserted.next_task_number`.
- Keep the `OUTPUT` target compatible with triggers: on tables with triggers use `OUTPUT ... INTO @table`.

- [ ] **Step 3: Run tests, confirm pass, commit**

### Task 2: Upserts and other raw SQL

**Files:** every other raw-SQL file in `src`:
- Modify: `Repositories/Auth/Login/EfGlobalEmailDirectoryRepository.cs`, `EfPasswordResetTokenRepository.cs`
- Modify: `Repositories/Calendar/EfCalendarEventMeetingRepository.cs`
- Modify: `Repositories/CoreHr/EfPositionAssignmentRepository.cs`
- Modify: `Repositories/DevPlatform/AppCatalog/EfGlobalApplicationRepository.cs`, `PlatformAccess/EfPlatformUserCredentialRepository.cs`
- Modify: `Repositories/OrgStructure/Department/EfDepartmentRepository.cs`, `Position/EfPositionRepository.cs`
- Modify: `Repositories/Storage/File/EfFileUploadReservationRepository.cs`, `Quota/EfTenantStorageStatsRepository.cs`
- Modify: `Repositories/WorkManagement/Meetings/EfMeetingAttendanceRepository.cs`, `EfMeetingLinkRepository.cs`, `EfMeetingOccurrenceWriter.cs`
- Modify: `Seeders/DevSmokeTestTenantSeeder.cs`, `Services/WorkManagement/Meetings/MeetingsStartupCheck.cs`, `src/ONEVO.Api/Controllers/Tenant/Attendance/LegalEntityClockInPoliciesController.cs`
- Modify: 4 `EF.Functions.ILike` sites: `EfConversationRepository.cs:523`, `EfTenancyRepository.cs:72-73`, and `.ToLower()` comment sites
- Test: `tests/ONEVO.Tests.SqlServer/RawSqlBehaviourTests.cs`
- Test: `tests/ONEVO.Tests.Architecture/NoPostgresSqlArchitectureTests.cs`

- [ ] **Step 1: Write `NoPostgresSqlArchitectureTests.NonMigrationSource_HasNoPostgresSql`**

Scan `src/**/*.cs`, ignoring comments, for `ON CONFLICT`, `RETURNING`, `SKIP LOCKED`, `FOR UPDATE`, `pg_`, `::uuid`, `::text`, `::jsonb`, `::timestamptz`, `set_config`, `current_setting`, `ILIKE`, `now()`, `= ANY(`, `unnest(`, `Npgsql`, `OPTION (MAXDOP`. Expected to fail on the listed files.

- [ ] **Step 2: Write `RawSqlBehaviourTests`,** one test per method rewritten: its insert path, its conflict path, and a wrong-tenant call that writes nothing. Search tests: `Search_IsCaseInsensitive` for each `ILike` site.

- [ ] **Step 3: Rewrite each file.** Upsert pattern: insert, catch `SqlServerErrors.IsUniqueViolation`, then read or update. Alternative where insert-first would be noisy: `UPDATE`, then `INSERT` under `UPDLOCK, HOLDLOCK` in a transaction. Use MERGE only if the spike proved it. `ILike` becomes `EF.Functions.Collate(col, "Latin1_General_100_CI_AS_SC").Contains(term)`, escaping `%`, `_` and `[` the same way as the original pattern.

- [ ] **Step 4: Run both test classes, confirm pass, commit**

### Task 3: Meeting pair rules move to the app

Deferred constraint triggers do not exist on SQL Server. The user approved moving them into the app (spec section 8).

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Meetings/MeetingIntegrityChecks.cs`
- Modify: `EfMeetingOccurrenceWriter.cs` and every writer of `tasks.task_kind`, `meeting_details` and placement rows
- Test: `tests/ONEVO.Tests.SqlServer/MeetingIntegrityTests.cs`
- Test: `tests/ONEVO.Tests.Architecture/MeetingWritePathArchitectureTests.cs`

**Interfaces:**
- Produces: `MeetingIntegrityChecks.EnsureAsync(ApplicationDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)`: runs inside the caller's transaction, before commit; throws `InvalidOperationException` with the original trigger's message text.

- [ ] **Step 1: Read the four original functions** in `20261018092918_AddMeetingWorkItemsFoundation.cs` (from git history): `meeting_task_has_details`, `meeting_details_has_meeting_task`, `meeting_placement_matches_series`, and the left-meeting-kind rule. Each rule becomes one check.

- [ ] **Step 2: Write the tests,** one per rule: meeting task without details fails and rolls back; details on a non-meeting task fails; task leaving meeting kind while details exist fails; placement not matching series fails; valid pair commits.

`MeetingWritePathArchitectureTests.OnlyAllowedWriters_TouchMeetingTables`: only the allowlisted classes write those entities. This partly offsets the loss of the database check.

- [ ] **Step 3: Implement, run tests, confirm pass, commit**

### Task 4: Version tokens

**Files:**
- Modify: `IEmployeeRepository.cs:83-91`, `EfEmployeeRepository.cs:882-888`, `IOnboardingDraftRepository.cs`, `EfOnboardingDraftRepository.cs:183`, `ILeaveCancellationRepository.cs`, `EfLeaveCancellationRepository.cs:95`, `GetMyProfileQueryHandler.cs:68`, `UpdatePersonalInformationCommandHandler.cs`, `OnboardingDraftWriteService.cs:239`, `CancelLeaveRequestCommand.cs:141`
- Create: `src/ONEVO.Infrastructure/Persistence/SqlServer/RowVersionToken.cs`
- Test: `tests/ONEVO.Tests.Unit/Persistence/RowVersionTokenTests.cs`, `tests/ONEVO.Tests.SqlServer/VersionTokenTests.cs`

**Interfaces:**
- Produces: `RowVersionToken.ToToken(byte[] rowVersion) -> string` and `RowVersionToken.TryParse(string? token, out byte[] rowVersion) -> bool`.
- `GetVersionTokenAsync` returns `Task<string?>`, not `Task<uint?>`.

- [ ] **Step 1: Check how the web app, admin console and tray use each token** (`C:\onevoNew\Hrms--Web-application---front-end---v1`, `HRMS-Platform-Administration-Front-End-v1`, `HRMS_TrayApp`, read only). A 64-bit number is unsafe in JavaScript, so a string is the default. If any client parses it as a number, tell the user: that client needs a change outside this repo.

- [ ] **Step 2: Write the tests:** round trip; junk token keeps today's silent no-op; `StaleToken_RaisesConcurrencyConflict` and `CurrentToken_Saves` for employee, draft and leave cancellation.

- [ ] **Step 3: Implement, run tests, confirm pass, commit**
