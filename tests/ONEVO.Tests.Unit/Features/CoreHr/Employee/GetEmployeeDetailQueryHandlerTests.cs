using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
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
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-15T12:00:00Z");

    public GetEmployeeDetailQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
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
            _clock.Object);

    private void ArrangeVisibleEmployee()
    {
        var visible = new EmployeeListItemResponse(
            _employeeId, "E-001", "Ada Lovelace", "ada@test.dev",
            null, null, null, null, null, null, "full_time", "active", null, null);
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
                HireDate = new DateOnly(2024, 1, 15)
            });
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
}
