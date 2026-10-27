using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;
using ProductionPlacement = ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// Spec 5.2 / D8: the bulk invite preview. The organizer picks a department or a manager's direct reports; the
/// preview expands them into a snapshot of individual people and reports the counts. The gate is the same as
/// the meeting's own, and above the provider's capacity the organizer gets a warning - never a cap.
/// </summary>
public sealed class MeetingBulkPreviewIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed class World
    {
        public MyTeamDb H = null!;
        public Guid Organizer;
        public Guid Attendee;
        public Guid Stranger;
        public Guid ProjectId;
        public Guid RootModuleId;
        public Guid DepartmentId;
        public Guid SubDepartmentId;
        public Guid Manager;
        public Guid DirectReport;
        public Guid DepartmentMember;
        public Guid SubDepartmentMember;
        public Guid Leaver;
        public MeetingSeries Series = null!;
    }

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();

        var organizer = h.AddEmployee(db);
        var attendee = h.AddEmployee(db);
        var stranger = h.AddEmployee(db);
        var (projectId, rootId) = h.AddProject(db, organizer.Id);

        var departmentId = h.AddDepartment(db);
        var subDepartmentId = h.AddDepartment(db, parentId: departmentId);

        var manager = h.AddEmployee(db);
        var directReport = h.AddEmployee(db);
        var departmentMember = h.AddEmployee(db, departmentId: departmentId);
        var subDepartmentMember = h.AddEmployee(db, departmentId: subDepartmentId);
        var leaver = h.AddEmployee(db);
        leaver.EmploymentStatusId = EmploymentStatusIds.Terminated;

        // The manager's direct report: the closure row is the reporting line the expansion reads.
        db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
        {
            TenantId = h.TenantId, AncestorEmployeeId = manager.Id, DescendantEmployeeId = directReport.Id, Depth = 1,
            SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
        });

        var at = new MeetingPlacement(organizer.Id, projectId, rootId, h.AddStatus(db, projectId, marksComplete: false));
        var series = h.AddSeries(db, at, First);
        h.AddSeriesParticipant(db, series, organizer.Id, CalendarEventParticipantStatuses.Accepted);

        await db.SaveChangesAsync();

        return new World
        {
            H = h, Organizer = organizer.Id, Attendee = attendee.Id, Stranger = stranger.Id,
            ProjectId = projectId, RootModuleId = rootId, DepartmentId = departmentId, SubDepartmentId = subDepartmentId,
            Manager = manager.Id, DirectReport = directReport.Id, DepartmentMember = departmentMember.Id,
            SubDepartmentMember = subDepartmentMember.Id, Leaver = leaver.Id, Series = series,
        };
    }

    private static MeetingBulkInviteService Service(ApplicationDbContext db, MeetingsOptions options, World w)
        => new(Options.Create(options),
            new CommonEfEmployeeRepository(db),
            new EfDepartmentRepository(db),
            new EfMeetingRepository(db),
            new PlacementResolver(db, w),
            new MeetingAccessResolver(new EfMeetingRepository(db)));

    private static MeetingBulkPreviewRequest Request(World w, Guid? seriesId = null, string? platform = null,
        IReadOnlyList<Guid>? departments = null, IReadOnlyList<Guid>? managers = null, IReadOnlyList<Guid>? people = null)
        => new(w.ProjectId, w.RootModuleId, departments ?? [], managers ?? [], people ?? [], seriesId, platform);

    /// <summary>The real create gate over the seeded tree: the organizer owns the root module, a stranger does not.</summary>
    private sealed class PlacementResolver : IMeetingPlacementResolver
    {
        private readonly MeetingPlacementResolver _real;

        public PlacementResolver(ApplicationDbContext db, World w)
        {
            var objectives = new EfObjectiveRepository(db);
            var membership = new ONEVO.Application.Features.WorkManagement.Objectives.Services.MilestoneMembershipCoordinator(
                new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), objectives,
                new EfProjectMemberInvitationRepository(db),
                new ONEVO.Application.Features.WorkManagement.Common.Services.CallerIdentityResolver(
                    new CommonEfEmployeeRepository(db), new EfEntityAssetRepository(db)),
                new ONEVO.Infrastructure.Services.SharedPlatform.Outbox.OutboxWriter(
                    db, new ONEVO.Infrastructure.Security.NoOpEncryptionService(), new SystemDateTimeProvider()));

            _real = new MeetingPlacementResolver(
                new ONEVO.Application.Features.WorkManagement.Hierarchy.WorkHierarchyService(objectives, membership),
                membership, new EfProjectRepository(db), objectives,
                new StubCurrentUser(w.H.TenantId), new StubOfficeReconciler());
        }

        public Task<Result<ProductionPlacement>> ResolveAsync(
            Guid tenantId, Guid callerEmployeeId, Guid? projectId, Guid? objectiveId, CancellationToken ct)
            => _real.ResolveAsync(tenantId, callerEmployeeId, projectId, objectiveId, ct);
    }

    private sealed class StubCurrentUser(Guid tenantId) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId => tenantId;
        public string Email => "organizer@bulk-preview-test.onevo.dev";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private sealed class StubOfficeReconciler : IOfficeProjectReconciler
    {
        public Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(OfficeProjectReconcileOutcome.AlreadyAligned);

        public Task<OfficeProjectReconcileOutcome?> TryReconcileAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<OfficeProjectReconcileOutcome?>(OfficeProjectReconcileOutcome.AlreadyAligned);
    }

    [Fact]
    public async Task Preview_ExpandsADepartmentAndAManagersDirectReports()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await Service(db, new MeetingsOptions { Create = true, BulkInvite = true }, w)
            .PreviewAsync(w.H.TenantId, w.Organizer, Request(w,
                departments: [w.DepartmentId], managers: [w.Manager]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var preview = result.Value!;
        // The department's own employees and the manager's direct report - the sub-department is not expanded.
        Assert.Equal(new[] { w.DepartmentMember, w.DirectReport }.Order(),
            preview.People.Select(p => p.EmployeeId).Order());
        Assert.Equal(2, preview.Selected);
        Assert.Equal(0, preview.DuplicatesRemoved);
        Assert.Equal(0, preview.InactiveExcluded);
        Assert.Equal(0, preview.AlreadyInvited);
        Assert.Null(preview.CapacityWarning);
    }

    [Fact]
    public async Task DirectReports_OfSeveralManagers_AreReadTogether()
    {
        var w = await SeedAsync();
        Guid secondManager, secondReport, grandReport;
        await using (var db = w.H.NewContext())
        {
            secondManager = w.H.AddEmployee(db).Id;
            secondReport = w.H.AddEmployee(db).Id;
            grandReport = w.H.AddEmployee(db).Id;
            db.EmployeeHierarchyClosures.AddRange(
                new EmployeeHierarchyClosure
                {
                    TenantId = w.H.TenantId, AncestorEmployeeId = secondManager, DescendantEmployeeId = secondReport, Depth = 1,
                    SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
                },
                // Two levels below the first manager: not a direct report, so not part of their team.
                new EmployeeHierarchyClosure
                {
                    TenantId = w.H.TenantId, AncestorEmployeeId = w.Manager, DescendantEmployeeId = grandReport, Depth = 2,
                    SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        await using var check = w.H.NewContext();
        var reports = await new CommonEfEmployeeRepository(check)
            .ListDirectReportEmployeeIdsAsync(w.H.TenantId, [w.Manager, secondManager], CancellationToken.None);

        Assert.Equal(new[] { w.DirectReport, secondReport }.Order(), reports.Order());
    }

    [Theory]
    [InlineData("departments")]
    [InlineData("managers")]
    [InlineData("people")]
    public async Task Preview_AListLongerThanTheLimit_Is400(string list)
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();
        var tooMany = Enumerable.Range(0, MeetingBulkInviteService.MaxIdsPerList + 1).Select(_ => Guid.NewGuid()).ToList();

        var result = await Service(db, new MeetingsOptions { Create = true, BulkInvite = true }, w)
            .PreviewAsync(w.H.TenantId, w.Organizer, list switch
            {
                "departments" => Request(w, departments: tooMany),
                "managers" => Request(w, managers: tooMany),
                _ => Request(w, people: tooMany),
            }, CancellationToken.None);

        Assert.Equal((400, MeetingErrorCodes.BulkSelectionTooLarge), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task Preview_CountsAlreadyInvited_IncludingTheOrganizer()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            w.H.AddSeriesParticipant(db, w.Series, w.Attendee);
            await db.SaveChangesAsync();
        }

        await using var check = w.H.NewContext();
        var result = await Service(check, new MeetingsOptions { Create = true, BulkInvite = true }, w)
            .PreviewAsync(w.H.TenantId, w.Organizer,
                Request(w, seriesId: w.Series.Id, people: [w.Organizer, w.Attendee]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var preview = result.Value!;
        // The organizer and the person already on the series are counted, not offered again.
        Assert.Equal(2, preview.AlreadyInvited);
        Assert.Empty(preview.People);
    }

    [Fact]
    public async Task Preview_AGroupHoldingTheOrganizerAndAnInvitee_CountsThemAsAlreadyInvited_Once()
    {
        // Review Focus 4 through groups: the department holds the organizer and someone already on the series, and the
        // manager's team holds that invitee again. Neither is offered; the repeat is a duplicate, not a second invite.
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var organizer = await db.Employees.SingleAsync(e => e.Id == w.Organizer);
            var attendee = await db.Employees.SingleAsync(e => e.Id == w.Attendee);
            organizer.DepartmentId = w.DepartmentId;
            attendee.DepartmentId = w.DepartmentId;
            db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
            {
                TenantId = w.H.TenantId, AncestorEmployeeId = w.Manager, DescendantEmployeeId = w.Attendee, Depth = 1,
                SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
            });
            w.H.AddSeriesParticipant(db, w.Series, w.Attendee);
            await db.SaveChangesAsync();
        }

        await using var check = w.H.NewContext();
        var result = await Service(check, new MeetingsOptions { Create = true, BulkInvite = true }, w)
            .PreviewAsync(w.H.TenantId, w.Organizer, Request(w, seriesId: w.Series.Id,
                departments: [w.DepartmentId], managers: [w.Manager]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var preview = result.Value!;
        Assert.Equal(new[] { w.DepartmentMember, w.DirectReport }.Order(), preview.People.Select(p => p.EmployeeId).Order());
        Assert.Equal(2, preview.AlreadyInvited);       // the organizer and the invitee, each counted once
        Assert.Equal(1, preview.DuplicatesRemoved);    // the invitee, in both groups
        Assert.Equal(5, preview.Selected);
    }

    [Fact]
    public async Task Preview_FlagOff_Is409BulkInviteUnavailable()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await Service(db, new MeetingsOptions { Create = true, BulkInvite = false }, w)
            .PreviewAsync(w.H.TenantId, w.Organizer, Request(w, departments: [w.DepartmentId]), CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.BulkInviteUnavailable), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task Preview_SameGateAsCreate_NonMemberIs403()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await Service(db, new MeetingsOptions { Create = true, BulkInvite = true }, w)
            .PreviewAsync(w.H.TenantId, w.Stranger, Request(w, departments: [w.DepartmentId]), CancellationToken.None);

        Assert.Equal((403, MeetingErrorCodes.ModuleMemberRequired), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task Preview_AboveProviderCapacity_WarnsButReturnsEveryone()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await Service(db, new MeetingsOptions
        {
            Create = true,
            BulkInvite = true,
            ProviderCapacityWarning = new Dictionary<string, int> { ["Zoom"] = 1 },
        }, w).PreviewAsync(w.H.TenantId, w.Organizer,
            Request(w, departments: [w.DepartmentId], platform: "zoom"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var preview = result.Value!;
        Assert.Single(preview.People);                                   // nobody is dropped
        // The organizer is already on the meeting, so the meeting will have two people.
        Assert.Equal("This meeting will have 2 people; Zoom meetings support up to 1.", preview.CapacityWarning);
    }

    [Fact]
    public async Task CreateWithThePreviewedIds_SkipsSomeoneWhoBecameInactive()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        // The preview sees the leaver as inactive and leaves them out of the people.
        var result = await Service(db, new MeetingsOptions { Create = true, BulkInvite = true }, w)
            .PreviewAsync(w.H.TenantId, w.Organizer, Request(w, people: [w.DepartmentMember, w.Leaver]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.InactiveExcluded);
        Assert.Equal(new[] { w.DepartmentMember }, result.Value.People.Select(p => p.EmployeeId));

        // The create re-validates: a leaver who slipped through is skipped, not invited.
        var factory = new MeetingTestServices(db, new MeetingTestClock(First.AddDays(-1))).Factory;
        var created = await new UnitOfWork(db).ExecuteInTransactionAsync(ct => factory.CreateDirectAsync(
            w.H.TenantId, w.Organizer, Guid.NewGuid(),
            new ProductionPlacement(w.ProjectId, w.RootModuleId, false, true),
            new CreateMeetingRequest(w.ProjectId, w.RootModuleId, "Delivery sync", null, null, false,
                First.AddDays(1), 30, false, "UTC", null, [w.DepartmentMember, w.Leaver], [], null),
            MeetingCreationPaths.Direct, approvalRequestId: null, ct));

        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal([(w.Leaver, "inactive")], created.Value!.Skipped.Select(s => (s.EmployeeId, s.Reason)));
    }
}
