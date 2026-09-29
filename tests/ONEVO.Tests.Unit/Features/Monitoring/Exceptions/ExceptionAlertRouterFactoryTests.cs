using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

/// <summary>
/// Runs the real IEmployeeAuthorityResolver behind both halves of the alert flow, for a manager
/// who is the employee's approver only through the reporting line (no management coverage):
/// the nightly job (no signed-in user) must still route the alert to them, and once they open
/// it the same manager must be able to see and resolve the case.
/// </summary>
public class ExceptionAlertRouterFactoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _managerPositionId = Guid.NewGuid();
    private readonly Employee _subject;
    private readonly Employee _manager;

    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeHierarchyClosureRepository> _closure = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IDepartmentRepository> _departments = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = DateTimeOffset.UtcNow };

    public ExceptionAlertRouterFactoryTests()
    {
        _subject = new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = Guid.NewGuid(),
            FirstName = "Nevi", LastName = "S", LegalEntityId = _legalEntityId
        };
        _manager = new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = Guid.NewGuid(),
            FirstName = "Mathu", LastName = "S", LegalEntityId = _legalEntityId
        };
        var managerAssignment = new PositionAssignment
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _manager.Id, PositionId = _managerPositionId
        };
        var everyone = new Dictionary<Guid, Employee> { [_subject.Id] = _subject, [_manager.Id] = _manager };

        // Every repository only answers for the one tenant, so a lost tenant id finds nobody.
        _employees.Setup(e => e.GetByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => everyone.GetValueOrDefault(id));
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _manager.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_manager);
        _employees.Setup(e => e.GetByUserAndLegalEntityAsync(_tenantId, _manager.UserId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_manager);
        _employees.Setup(e => e.ListByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(everyone);
        _employees.Setup(e => e.ListActiveEmployeeIdsByIdsAsync(_tenantId, _legalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.ToList());

        // The manager sits above the subject on the reporting line only.
        _closure.Setup(c => c.GetAncestorChainEmployeeIdsAsync(_tenantId, _subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_manager.Id]);
        _closure.Setup(c => c.GetDescendantEmployeeIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _closure.Setup(c => c.GetAncestorChainsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>> { [_subject.Id] = [_manager.Id] });

        _assignments.Setup(a => a.GetActivePrimaryAsync(_tenantId, _manager.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerAssignment);
        _assignments.Setup(a => a.GetActivePrimaryByEmployeeIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionAssignment> { [_manager.Id] = managerAssignment });
        _assignments.Setup(a => a.GetActiveHoldersByPositionIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PositionActiveHolder>>());

        // No management coverage anywhere.
        _positions.Setup(p => p.ListCoverageByOwnerPositionAsync(_tenantId, _legalEntityId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _positions.Setup(p => p.ListActivePositionCoverageByCoveredPositionIdsAsync(_tenantId, _legalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>());
        _positions.Setup(p => p.ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync(_tenantId, _legalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>());
        _positions.Setup(p => p.GetByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _permissions.Setup(p => p.UserHasPermissionCodeAsync(
                _manager.UserId, ExceptionPermissions.ManagerReview, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _permissions.Setup(p => p.ListUserIdsHoldingPermissionAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), ExceptionPermissions.ManagerReview, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { _manager.UserId });
    }

    private DomainException CaseFor(Guid employeeId) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employeeId,
        Type = ExceptionType.SustainedLowActivity, Status = ExceptionStatus.Open,
        Title = "Sustained low activity", Description = "desc", DetectedAt = DateTimeOffset.UtcNow
    };

    private ICurrentUser SignedInManager()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.TenantId).Returns(_tenantId);
        user.SetupGet(u => u.UserId).Returns(_manager.UserId);
        user.Setup(u => u.HasPermission(It.IsAny<string>()))
            .Returns((string p) => p == ExceptionPermissions.ManagerReview);
        return user.Object;
    }

    private ExceptionScopeResolver ScopeFor(ICurrentUser user) => new(
        user, _employees.Object,
        new EmployeeAuthorityResolver(user, _clock, _employees.Object, _assignments.Object,
            _positions.Object, _closure.Object, _departments.Object, _permissions.Object));

    [Fact]
    public async Task BackgroundRouter_ResolvesReportingLineManager_WithoutASignedInUser()
    {
        var factory = new ExceptionAlertRouterFactory(
            _clock, _employees.Object, _assignments.Object, _positions.Object,
            _closure.Object, _departments.Object, _permissions.Object, _notifications.Object);

        await factory.CreateForTenant(_tenantId).NotifyDetectedAsync(CaseFor(_subject.Id), CancellationToken.None);

        _notifications.Verify(n => n.SendTemplatedAsync(
            _tenantId, _manager.UserId, ExceptionPermissions.DetectedTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(), ExceptionPermissions.RelatedEntityType,
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportingLineManager_WhoWasAlerted_CanSeeAndResolveTheCase_ButNotOthers()
    {
        var user = SignedInManager();
        var stranger = Guid.NewGuid();

        var scope = await ScopeFor(user).ResolveAsync(forAction: false, [_subject.Id, stranger], CancellationToken.None);

        scope!.CanSee(_subject.Id).Should().BeTrue();
        scope.CanSee(stranger).Should().BeFalse();

        var @case = CaseFor(_subject.Id);
        var exceptions = new Mock<IExceptionRepository>();
        exceptions.Setup(e => e.GetByIdAsync(_tenantId, @case.Id, It.IsAny<CancellationToken>())).ReturnsAsync(@case);
        var handler = new ResolveExceptionCommandHandler(exceptions.Object, user, ScopeFor(user), _clock);

        var result = await handler.Handle(new ResolveExceptionCommand(@case.Id, "Spoke with them."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        @case.Status.Should().Be(ExceptionStatus.Resolved);
    }
}
