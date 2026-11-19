using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeeEmploymentType;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class ChangeEmployeeEmploymentTypeCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeManageScopeGuard> _scope = new();
    private readonly Mock<IEmployeeOffboardingLockGuard> _lock = new();
    private readonly Mock<IEmploymentTypeRepository> _types = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ONEVO.Domain.Features.CoreHr.Entities.Employee _employee;

    public ChangeEmployeeEmploymentTypeCommandHandlerTests()
    {
        _employee = new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeNumber = "E-007", EmploymentTypeId = 1, WorkModeId = Guid.NewGuid()
        };
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _employees.Setup(e => e.GetTrackedByIdAsync(_tenantId, _employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_employee);
        _types.Setup(t => t.GetIdByCodeAsync("contract", It.IsAny<CancellationToken>())).ReturnsAsync(3);
    }

    private ChangeEmployeeEmploymentTypeCommandHandler CreateSut() =>
        new(_employees.Object, _scope.Object, _lock.Object, _types.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_Valid_ChangesOnlyEmploymentType()
    {
        var workMode = _employee.WorkModeId;

        var result = await CreateSut().Handle(new ChangeEmployeeEmploymentTypeCommand(_employee.Id, "contract"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _employee.EmploymentTypeId);
        Assert.Equal("E-007", _employee.EmployeeNumber);
        Assert.Equal(workMode, _employee.WorkModeId);
        _employees.Verify(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownEmployee_ReturnsNotFound()
    {
        var result = await CreateSut().Handle(new ChangeEmployeeEmploymentTypeCommand(Guid.NewGuid(), "contract"), CancellationToken.None);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_OutOfScope_ReturnsForbidden_WithoutSaving()
    {
        _scope.Setup(s => s.EnsureCanManage(_tenantId, _employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Forbidden("You do not have access to manage this employee."));

        var result = await CreateSut().Handle(new ChangeEmployeeEmploymentTypeCommand(_employee.Id, "contract"), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _employees.Verify(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Offboarded_ReturnsConflict()
    {
        _lock.Setup(l => l.EnsureMutable(_tenantId, _employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Conflict("This employee's record is read-only after offboarding completion."));

        var result = await CreateSut().Handle(new ChangeEmployeeEmploymentTypeCommand(_employee.Id, "contract"), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnknownCode_ReturnsBadRequest()
    {
        var result = await CreateSut().Handle(new ChangeEmployeeEmploymentTypeCommand(_employee.Id, "nope"), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Employment type is invalid.", result.Error);
    }
}
