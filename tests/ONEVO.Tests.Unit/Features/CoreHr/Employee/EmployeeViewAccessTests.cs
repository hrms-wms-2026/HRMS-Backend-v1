using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using VisibleEmployeeRepo = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EmployeeViewAccessTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<CommonEmployeeRepo> _employees = new();
    private readonly Mock<VisibleEmployeeRepo> _visibleEmployees = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<IInvitationTokenRepository> _invitations = new();
    private readonly Mock<IEmployeeInvitationAccess> _invitationAccess = new();
    private readonly EmployeeVisibilityScope _scope;

    public EmployeeViewAccessTests()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(_userId);
        // No permission codes at all: visibility must come from the relationship alone.
        _currentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns(false);
        _employees.Setup(r => r.GetByUserIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });
        _scope = new EmployeeVisibilityScope(
            false, _employeeId, new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>());
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_scope);
    }

    private EmployeeViewAccess Sut() =>
        new(_employees.Object, _visibleEmployees.Object, _scopeResolver.Object,
            _invitations.Object, _invitationAccess.Object, _currentUser.Object);

    private Guid AddInvitedEmployee(Guid inviterUserId, DateTimeOffset? usedAt = null, DateTimeOffset? revokedAt = null)
    {
        var id = Guid.NewGuid();
        var createdById = Guid.NewGuid();
        var invitation = new InvitationToken { TenantId = _tenantId, EmployeeId = id, UsedAt = usedAt, RevokedAt = revokedAt };
        _employees.Setup(r => r.GetByIdAsync(_tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = id, TenantId = _tenantId, CreatedById = createdById });
        _invitations.Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);
        _invitationAccess.Setup(a => a.ResolveInviterUserIdAsync(invitation, createdById, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterUserId);
        return id;
    }

    [Fact]
    public async Task OpenInviteYouSent_IsVisible_WithoutCoverage()
    {
        // A just-invited employee's position is still "planned", so no reporting line reaches
        // them; the inviter must still be able to open them (detail page + widgets).
        var invited = AddInvitedEmployee(_userId);

        Assert.True(await Sut().CanViewAsync(invited));
    }

    [Fact]
    public async Task OpenInviteSomeoneElseSent_IsNotVisible()
    {
        var invited = AddInvitedEmployee(Guid.NewGuid());

        Assert.False(await Sut().CanViewAsync(invited));
    }

    [Fact]
    public async Task AcceptedInviteYouSent_FallsBackToCoverage()
    {
        var accepted = AddInvitedEmployee(_userId, usedAt: DateTimeOffset.UtcNow);

        Assert.False(await Sut().CanViewAsync(accepted));
    }

    private void MakeVisible(Guid employeeId, string status = "active") =>
        _visibleEmployees.Setup(r => r.GetVisibleByIdAsync(_tenantId, _scope, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeListItemResponse(employeeId, "E-1", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", status, null, null));

    [Fact]
    public async Task Self_IsAlwaysVisible()
    {
        Assert.True(await Sut().CanViewAsync(_employeeId));
    }

    [Fact]
    public async Task EmployeeInsideCoverage_IsVisible_WithoutAnyPermission()
    {
        var report = Guid.NewGuid();
        MakeVisible(report);

        Assert.True(await Sut().CanViewAsync(report));
    }

    [Fact]
    public async Task OffboardingReport_StaysVisible()
    {
        // Starting offboarding flips the status to "offboarding"; the manager must still be able
        // to load the offboarding wizard (GET offboarding, checklist-matches) afterwards.
        var leaver = Guid.NewGuid();
        MakeVisible(leaver, status: "offboarding");

        Assert.True(await Sut().CanViewAsync(leaver));
    }

    [Fact]
    public async Task EmployeeOutsideCoverage_IsNotVisible()
    {
        Assert.False(await Sut().CanViewAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Unauthenticated_IsNotVisible()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);

        Assert.False(await Sut().CanViewAsync(_employeeId));
    }
}
