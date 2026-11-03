using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>D4: the module owner or an ancestor's owner creates directly; another member requests approval; a
/// non-member gets 403. Office meetings need calendar:write and land at the Office project's root.</summary>
public sealed class MeetingPlacementResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid RootOwner = Guid.NewGuid();
    private static readonly Guid ChildOwner = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private readonly Objective _root = new() { Id = Guid.NewGuid(), ProjectId = Project, OwnerId = RootOwner, IsActive = true };
    private readonly Objective _child;
    private readonly Objective _inactive;
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IOfficeProjectReconciler> _reconciler = new();

    public MeetingPlacementResolverTests()
    {
        _child = new Objective { Id = Guid.NewGuid(), ProjectId = Project, ParentObjectiveId = _root.Id, OwnerId = ChildOwner, IsActive = true };
        _inactive = new Objective { Id = Guid.NewGuid(), ProjectId = Project, ParentObjectiveId = _root.Id, OwnerId = ChildOwner, IsActive = false };
        _hierarchy.Setup(h => h.LoadTreeAsync(Tenant, Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree([_root, _child, _inactive]));
        _membership.Setup(m => m.IsEffectiveManagerAsync(Tenant, _child.Id, Member, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private MeetingPlacementResolver Resolver() => new(_hierarchy.Object, _membership.Object, _projects.Object,
        _objectives.Object, _user.Object, _reconciler.Object);

    [Fact]
    public async Task TheModuleOwner_AndAnAncestorsOwner_CreateDirectly()
    {
        var own = await Resolver().ResolveAsync(Tenant, ChildOwner, Project, _child.Id, CancellationToken.None);
        var ancestor = await Resolver().ResolveAsync(Tenant, RootOwner, Project, _child.Id, CancellationToken.None);

        Assert.True(own.Value!.Direct);
        Assert.True(ancestor.Value!.Direct);
        Assert.Equal((Project, _child.Id, false), (own.Value.ProjectId, own.Value.ObjectiveId, own.Value.IsOffice));
    }

    [Fact]
    public async Task AnotherMember_NeedsApproval()
        => Assert.False((await Resolver().ResolveAsync(Tenant, Member, Project, _child.Id, CancellationToken.None)).Value!.Direct);

    [Fact]
    public async Task ANonMember_Gets403()
    {
        var result = await Resolver().ResolveAsync(Tenant, Stranger, Project, _child.Id, CancellationToken.None);

        Assert.Equal((403, MeetingErrorCodes.ModuleMemberRequired), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task AnInactiveOrMissingModule_IsUnavailable()
    {
        var inactive = await Resolver().ResolveAsync(Tenant, ChildOwner, Project, _inactive.Id, CancellationToken.None);
        var missing = await Resolver().ResolveAsync(Tenant, ChildOwner, Project, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(MeetingErrorCodes.ModuleUnavailable, inactive.ErrorCode);
        Assert.Equal(MeetingErrorCodes.ModuleUnavailable, missing.ErrorCode);
    }

    [Fact]
    public async Task Office_WithCalendarWrite_LandsAtTheOfficeRoot()
    {
        var office = new Project { Id = Guid.NewGuid(), SystemPurpose = ProjectSystemPurposes.Office };
        var officeRoot = new Objective { Id = Guid.NewGuid(), ProjectId = office.Id };
        _user.Setup(u => u.HasPermission("calendar:write")).Returns(true);
        _projects.Setup(p => p.GetOfficeProjectAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(office);
        _objectives.Setup(o => o.GetDefaultByProjectIdAsync(Tenant, office.Id, It.IsAny<CancellationToken>())).ReturnsAsync(officeRoot);

        var result = await Resolver().ResolveAsync(Tenant, Stranger, null, null, CancellationToken.None);

        Assert.Equal(new MeetingPlacement(office.Id, officeRoot.Id, IsOffice: true, Direct: true), result.Value);
    }

    [Fact]
    public async Task Office_WithoutCalendarWrite_Is403()
    {
        var result = await Resolver().ResolveAsync(Tenant, Stranger, null, null, CancellationToken.None);

        Assert.Equal((403, MeetingErrorCodes.CalendarWriteRequired), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task NoOfficeProject_IsNotReady_AndTriggersAReconcile()
    {
        _user.Setup(u => u.HasPermission("calendar:write")).Returns(true);

        var result = await Resolver().ResolveAsync(Tenant, Stranger, null, null, CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.OfficeNotReady), (result.StatusCode, result.ErrorCode));
        _reconciler.Verify(r => r.ReconcileAsync(Tenant, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false, null, null)]
    [InlineData(true, false, 503, MeetingErrorCodes.MeetingsDisabled)]
    [InlineData(true, true, 409, MeetingErrorCodes.MeetingsReadOnly)]
    public void Guard_FollowsTheFlags(bool createOn, bool readOnly, int? status, string? code)
    {
        var options = new MeetingsOptions { Create = createOn, ReadOnly = readOnly };
        var blocked = MeetingGuard.CheckWritable<int>(options);

        if (createOn && !readOnly)
            Assert.Null(blocked);
        else if (!createOn)
            Assert.Equal((503, MeetingErrorCodes.MeetingsDisabled), (blocked!.StatusCode, blocked.ErrorCode));
        else
            Assert.Equal((status, code), (blocked!.StatusCode, blocked.ErrorCode));
    }
}
