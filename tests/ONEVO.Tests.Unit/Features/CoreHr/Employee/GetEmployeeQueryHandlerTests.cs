using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<IInvitationTokenRepository> _invitationTokenRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-15T12:00:00Z");

    public GetEmployeeQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);
    }

    private GetEmployeeQueryHandler CreateHandler() =>
        new(_employeeRepository.Object, _scopeResolver.Object, _invitationTokenRepository.Object, _currentUser.Object, _clock.Object);

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenEmployeeDoesNotExistInTenant()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenEmployeeExistsButIsOutsideVisibilityScope()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        _scopeResolver
            .Setup(r => r.ResolveAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsEmployee_WhenVisible()
    {
        var response = new EmployeeListItemResponse(_employeeId, "E-001", "Ada Lovelace", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null);
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
    }

    [Theory]
    [InlineData(null, null, null, "pending")]
    [InlineData(1, null, null, "accepted")]
    [InlineData(null, 1, null, "revoked")]
    [InlineData(null, null, -1, "expired")]
    public async Task Handle_ComputesInvitationStatus_FromLatestInvitationForEmployee(
        int? usedAtHoursOffset, int? revokedAtHoursOffset, int? expiresAtDaysOffset, string expectedStatus)
    {
        var response = new EmployeeListItemResponse(_employeeId, "E-001", "Ada Lovelace", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null);
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var expiresAt = expiresAtDaysOffset.HasValue ? _now.AddDays(expiresAtDaysOffset.Value) : _now.AddDays(1);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationToken
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                ExpiresAt = expiresAt,
                UsedAt = usedAtHoursOffset.HasValue ? _now.AddHours(usedAtHoursOffset.Value) : null,
                RevokedAt = revokedAtHoursOffset.HasValue ? _now.AddHours(revokedAtHoursOffset.Value) : null,
            });

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedStatus, result.Value!.InvitationStatus);
        Assert.Equal(expiresAt, result.Value.InvitationExpiresAt);
    }

    [Fact]
    public async Task Handle_OutsideCoverage_ButCallerInvitedThemAndStillPending_ReturnsEmployee()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        _scopeResolver
            .Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        // Coverage-scoped lookup finds nothing - this employee is outside coverage.
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.Is<EmployeeVisibilityScope>(s => !s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);
        // The unrestricted re-fetch (used only once the invite exception is proven) succeeds.
        var response = new EmployeeListItemResponse(_employeeId, "E-001", "New Hire", "new.hire@test.dev", null, null, null, null, null, null, "full_time", "onboarding", null, null);
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationToken
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                CreatedById = _userId, ExpiresAt = _now.AddDays(1), UsedAt = null, RevokedAt = null,
            });

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_employeeId, result.Value!.Id);
        Assert.Equal("pending", result.Value.InvitationStatus);
    }

    [Fact]
    public async Task Handle_OutsideCoverage_InvitedBySomeoneElse_StillForbidden()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        _scopeResolver
            .Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationToken
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                CreatedById = Guid.NewGuid(), ExpiresAt = _now.AddDays(1), UsedAt = null, RevokedAt = null,
            });

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoInvitationEverIssued_LeavesInvitationStatusNull()
    {
        var response = new EmployeeListItemResponse(_employeeId, "E-001", "Ada Lovelace", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null);
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(_tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await CreateHandler().Handle(new GetEmployeeQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.InvitationStatus);
        Assert.Null(result.Value.InvitationExpiresAt);
    }
}
