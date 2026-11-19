using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.RevealEmployeeBankDetails;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using CoreHrEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class RevealEmployeeBankDetailsCommandHandlerTests
{
    private readonly Mock<CoreHrEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _visibility = new();
    private readonly Mock<IEmployeeProfileRepository> _profile = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IAuditLogRepository> _auditLogs = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-10-05T08:30:00Z");

    public RevealEmployeeBankDetailsCommandHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(_userId);
        _clock.SetupGet(x => x.UtcNow).Returns(_now);
    }

    [Fact]
    public async Task Handle_WithoutSensitivePermission_ReturnsForbiddenWithoutReadingBankDetails()
    {
        _currentUser.Setup(x => x.HasPermission("employees:read:sensitive")).Returns(false);

        var result = await CreateHandler().Handle(
            new RevealEmployeeBankDetailsCommand(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _profile.Verify(
            x => x.GetPrimaryBankDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _auditLogs.Verify(
            x => x.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeOutsideVisibilityScope_ReturnsForbiddenWithoutReadingBankDetails()
    {
        _currentUser.Setup(x => x.HasPermission("employees:read:sensitive")).Returns(true);
        _currentUser.Setup(x => x.HasPermission("org:manage")).Returns(false);
        _employees.Setup(x => x.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _visibility.Setup(x => x.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(
                false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        _employees.Setup(x => x.GetVisibleByIdAsync(
                _tenantId, It.IsAny<EmployeeVisibilityScope>(), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);

        var result = await CreateHandler().Handle(
            new RevealEmployeeBankDetailsCommand(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _profile.Verify(
            x => x.GetPrimaryBankDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AuthorizedVisibleEmployee_ReturnsFullDetailsAndWritesAuditRecord()
    {
        ArrangeAuthorizedVisibleEmployee();
        _profile.Setup(x => x.GetPrimaryBankDetailAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeBankDetail
            {
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                BankName = "Commercial Bank",
                BranchName = "Colombo",
                AccountHolderName = "Ada Lovelace",
                AccountNumberEncrypted = "cipher",
                AccountType = "Savings",
                RoutingNumber = "7010"
            });
        _encryption.Setup(x => x.Decrypt("cipher")).Returns("1234567890");

        var result = await CreateHandler().Handle(
            new RevealEmployeeBankDetailsCommand(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("1234567890", result.Value!.AccountNumber);
        Assert.Equal("Ada Lovelace", result.Value.AccountHolderName);
        Assert.Equal("Colombo", result.Value.BranchName);
        Assert.Equal("7010", result.Value.RoutingNumber);
        _auditLogs.Verify(x => x.AddAsync(
            It.Is<AuditLog>(log =>
                log.TenantId == _tenantId
                && log.UserId == _userId
                && log.Action == "employee.bank_details_revealed"
                && log.ResourceType == "Employee"
                && log.ResourceId == _employeeId
                && log.CreatedAt == _now),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private RevealEmployeeBankDetailsCommandHandler CreateHandler() => new(
        _employees.Object,
        _visibility.Object,
        _profile.Object,
        _encryption.Object,
        _currentUser.Object,
        _auditLogs.Object,
        _unitOfWork.Object,
        _clock.Object);

    private void ArrangeAuthorizedVisibleEmployee()
    {
        _currentUser.Setup(x => x.HasPermission("employees:read:sensitive")).Returns(true);
        _currentUser.Setup(x => x.HasPermission("org:manage")).Returns(true);
        _employees.Setup(x => x.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = _employeeId, TenantId = _tenantId });
        _employees.Setup(x => x.GetVisibleByIdAsync(
                _tenantId,
                It.Is<EmployeeVisibilityScope>(scope => scope.CanViewAllTenantEmployees),
                _employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada Lovelace", "ada@example.com",
                null, null, null, null, null, null, "Full-time", "active", null, null));
    }
}
