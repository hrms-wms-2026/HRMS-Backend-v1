using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.Models;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeDetailQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<IEmployeeProfileRepository> _profile = new();
    private readonly Mock<IInvitationTokenRepository> _invitationTokenRepository = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IEmploymentTypeRepository> _employmentTypes = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IProjectMemberRepository> _projectMembers = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _viewerEmployeeId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _workModeId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-15T12:00:00Z");

    public GetEmployeeDetailQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);
        _profile
            .Setup(p => p.ListAddressesAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _profile
            .Setup(p => p.ListEmergencyContactsAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private GetEmployeeDetailQueryHandler CreateHandler() =>
        new(
            _employeeRepository.Object,
            _scopeResolver.Object,
            _profile.Object,
            _invitationTokenRepository.Object,
            _encryption.Object,
            _currentUser.Object,
            _clock.Object,
            _employmentTypes.Object,
            _legalEntities.Object,
            _projectMembers.Object);

    private void ArrangeVisibleEmployee()
    {
        var visible = new EmployeeListItemResponse(
            _employeeId, "E-001", "Ada Lovelace", "ada@test.dev",
            null, null, null, null, null, null, "full_time", "active", null, null,
            WorkModeLabel: "Remote");
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee
            {
                Id = _employeeId,
                TenantId = _tenantId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@test.dev",
                EmployeeNumber = "E-001",
                HireDate = new DateOnly(2024, 1, 15),
                EmploymentTypeId = 1,
                WorkModeId = _workModeId
            });
        _employmentTypes
            .Setup(r => r.GetCodeByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync("full_time");
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _employeeRepository
            .Setup(r => r.GetVisibleByIdAsync(
                _tenantId,
                It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees),
                _employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(visible);
    }

    [Fact]
    public async Task Handle_CallerLacksSensitivePermission_OmitsPayroll()
    {
        ArrangeVisibleEmployee();
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(false);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Payroll);
        _profile.Verify(
            p => p.GetPrimaryBankDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_IncludesWorkModeLabelFromVisibleEmployeeProjection()
    {
        ArrangeVisibleEmployee();
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(false);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Remote", result.Value!.JobInformation.WorkModeLabel);
    }

    [Fact]
    public async Task Handle_IncludesEmploymentTypeCodeAndWorkModeIdForEditForm()
    {
        ArrangeVisibleEmployee();
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(false);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("full_time", result.Value!.JobInformation.EmploymentTypeCode);
        Assert.Equal(_workModeId, result.Value!.JobInformation.WorkModeId);
    }

    [Fact]
    public async Task Handle_CallerHasSensitivePermission_IncludesMaskedPayroll()
    {
        ArrangeVisibleEmployee();
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(true);
        _profile
            .Setup(p => p.GetPrimaryBankDetailAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeBankDetail
            {
                EmployeeId = _employeeId,
                BankName = "Test Bank",
                AccountNumberEncrypted = "cipher",
                AccountType = "checking",
                IsPrimary = true
            });
        _encryption.Setup(e => e.Decrypt("cipher")).Returns("1234567890");

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Payroll);
        Assert.True(result.Value!.Payroll!.HasBankDetailsOnFile);
    }

    [Fact]
    public async Task Handle_CallerLacksAttendanceReadPermission_OmitsAttendanceSummary()
    {
        ArrangeVisibleEmployee();
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(false);
        _currentUser.Setup(c => c.HasPermission("attendance:read")).Returns(false);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AttendanceSummary);
        _employeeRepository.Verify(
            r => r.ListVisibleAsync(
                It.IsAny<Guid>(), It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<EmployeeListAttendanceOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_CallerHasAttendanceReadPermission_IncludesAttendanceSummaryScopedToThisEmployee()
    {
        ArrangeVisibleEmployee();
        _currentUser.Setup(c => c.HasPermission("employees:read:sensitive")).Returns(false);
        _currentUser.Setup(c => c.HasPermission("attendance:read")).Returns(true);

        var summary = new EmployeeListAttendanceSummaryResponse(
            ShowNotClockedInWarning: true,
            ShouldHaveClockedIn: true,
            HasClockedInToday: false,
            WorkDate: DateOnly.FromDateTime(_now.UtcDateTime),
            Timezone: "UTC",
            ScheduledStartTime: "09:00",
            WarningLabel: "Still has not clocked in",
            AttendanceStatus: "not_clocked_in",
            AttendanceStatusLabel: "Not clocked in",
            AttentionType: "not_clocked_in",
            AttentionSeverity: "critical",
            AttentionLabel: "Still has not clocked in");

        var attendanceItem = new EmployeeListItemResponse(
            _employeeId, "E-001", "Ada Lovelace", "ada@test.dev",
            null, null, null, null, null, null, "full_time", "active", null, null,
            AttendanceSummary: summary);

        _employeeRepository
            .Setup(r => r.ListVisibleAsync(
                _tenantId,
                It.Is<EmployeeVisibilityScope>(s => !s.CanViewAllTenantEmployees),
                It.Is<EmployeeListFilter>(f =>
                    f.RestrictToEmployeeIds != null
                    && f.RestrictToEmployeeIds.Count == 1
                    && f.RestrictToEmployeeIds.Contains(_employeeId)),
                1, 1,
                It.IsAny<CancellationToken>(),
                It.Is<EmployeeListAttendanceOptions>(o => o.UtcNow == _now)))
            .ReturnsAsync((new[] { attendanceItem }, 1));

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.AttendanceSummary);
        Assert.Equal("not_clocked_in", result.Value!.AttendanceSummary!.AttentionType);
        Assert.True(result.Value!.AttendanceSummary!.ShowNotClockedInWarning);
    }

    [Fact]
    public async Task Handle_EmployeeOutsideVisibilityScope_ReturnsForbidden()
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

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsNotFound()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    private ONEVO.Domain.Features.CoreHr.Entities.Employee ArrangeViewer()
    {
        var viewer = new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _viewerEmployeeId, TenantId = _tenantId };
        _employeeRepository
            .Setup(r => r.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(viewer);
        return viewer;
    }

    [Fact]
    public async Task Handle_UsesEmployeeDisplayTimezoneWhenSet()
    {
        ArrangeVisibleEmployee();
        var employee = await _employeeRepository.Object.GetByIdAsync(_tenantId, _employeeId);
        employee!.DisplayTimezone = "Asia/Colombo";
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.Equal("Asia/Colombo", result.Value!.JobInformation.Timezone);
        _legalEntities.Verify(
            r => r.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FallsBackToLegalEntityTimezone()
    {
        ArrangeVisibleEmployee();
        var legalEntityId = Guid.NewGuid();
        var employee = await _employeeRepository.Object.GetByIdAsync(_tenantId, _employeeId);
        employee!.LegalEntityId = legalEntityId;
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _legalEntities
            .Setup(r => r.GetByIdForTenantAsync(_tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = legalEntityId, TenantId = _tenantId, Timezone = "Europe/London" });

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.Equal("Europe/London", result.Value!.JobInformation.Timezone);
    }

    [Fact]
    public async Task Handle_ReturnsNullTimezoneWhenNeitherEmployeeNorLegalEntityHasOne()
    {
        ArrangeVisibleEmployee();

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.Null(result.Value!.JobInformation.Timezone);
    }

    [Fact]
    public async Task Handle_ReturnsProjectMembershipsSharedWithTheViewer()
    {
        ArrangeVisibleEmployee();
        ArrangeViewer();
        var projectId = Guid.NewGuid();
        var since = DateTimeOffset.Parse("2025-08-15T00:00:00Z");
        _projectMembers
            .Setup(r => r.ListSharedProjectMembershipsAsync(_tenantId, _employeeId, _viewerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EmployeeProjectMembershipSummary(projectId, "Watercraft Platform", since, true)]);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        var membership = Assert.Single(result.Value!.ProjectMemberships!);
        Assert.Equal(new EmployeeDetailProjectMembership(projectId, "Watercraft Platform", since, true), membership);
    }

    [Fact]
    public async Task Handle_ReturnsNoProjectMembershipsWhenViewerHasNoEmployeeRecord()
    {
        ArrangeVisibleEmployee();
        _employeeRepository
            .Setup(r => r.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var result = await CreateHandler().Handle(new GetEmployeeDetailQuery(_employeeId), CancellationToken.None);

        Assert.Empty(result.Value!.ProjectMemberships!);
        _projectMembers.Verify(
            r => r.ListSharedProjectMembershipsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
