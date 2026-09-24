using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeePositionHistory;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeePositionHistoryQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentRepository = new();
    private readonly Mock<IPositionRepository> _positionRepository = new();
    private readonly Mock<IAccessGrantRequestRepository> _accessGrantRequestRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeePositionHistoryQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
    }

    private GetEmployeePositionHistoryQueryHandler CreateHandler() =>
        new(
            _employeeRepository.Object,
            _scopeResolver.Object,
            _currentUser.Object,
            _positionAssignmentRepository.Object,
            _positionRepository.Object,
            _accessGrantRequestRepository.Object,
            _userRepository.Object);

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
    public async Task Handle_ReturnsEntriesOldestFirst_WithApprovedByOnlyWhenApprovalRequestExists()
    {
        ArrangeVisibleEmployee();

        var requesterId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var hirePositionId = Guid.NewGuid();
        var promoPositionId = Guid.NewGuid();
        var hireAssignmentId = Guid.NewGuid();
        var promoAssignmentId = Guid.NewGuid();

        var hireAssignment = new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
        {
            Id = hireAssignmentId,
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            PositionId = hirePositionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Ended,
            EffectiveFrom = new DateOnly(2024, 1, 15),
            EffectiveTo = new DateOnly(2025, 6, 30),
            ChangeReason = null,
            CreatedById = Guid.NewGuid()
        };
        var promoAssignment = new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
        {
            Id = promoAssignmentId,
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            PositionId = promoPositionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Active,
            EffectiveFrom = new DateOnly(2025, 7, 1),
            EffectiveTo = null,
            ChangeReason = "Promotion",
            CreatedById = requesterId
        };

        _positionAssignmentRepository
            .Setup(r => r.ListHistoryForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment> { hireAssignment, promoAssignment });
        _positionRepository
            .Setup(r => r.GetByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new() { Id = hirePositionId, TenantId = _tenantId, Name = "Analyst" },
                new() { Id = promoPositionId, TenantId = _tenantId, Name = "Senior Analyst" }
            });
        _accessGrantRequestRepository
            .Setup(r => r.GetApprovedByUserIdsForAssignmentsAsync(
                _tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [promoAssignmentId] = approverId });
        _userRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>
            {
                new() { Id = requesterId, FirstName = "Riya", LastName = "Starter" },
                new() { Id = approverId, FirstName = "Alex", LastName = "Approver" }
            });

        var result = await CreateHandler().Handle(new GetEmployeePositionHistoryQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Count);

        var hire = result.Value[0];
        Assert.Equal("Analyst", hire.PositionName);
        Assert.Equal(new DateOnly(2024, 1, 15), hire.EffectiveFrom);
        Assert.Equal(new DateOnly(2025, 6, 30), hire.EffectiveTo);
        Assert.Null(hire.ChangeReason);
        Assert.Null(hire.ApprovedByName);

        var promo = result.Value[1];
        Assert.Equal("Senior Analyst", promo.PositionName);
        Assert.Equal(new DateOnly(2025, 7, 1), promo.EffectiveFrom);
        Assert.Null(promo.EffectiveTo);
        Assert.Equal("Promotion", promo.ChangeReason);
        Assert.Equal("Riya Starter", promo.InitiatedByName);
        Assert.Equal("Alex Approver", promo.ApprovedByName);
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

        var result = await CreateHandler().Handle(new GetEmployeePositionHistoryQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsNotFound()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var result = await CreateHandler().Handle(new GetEmployeePositionHistoryQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
