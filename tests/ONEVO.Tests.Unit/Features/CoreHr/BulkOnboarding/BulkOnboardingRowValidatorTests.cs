using Moq;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingRowValidatorTests
{
    private readonly Mock<IDepartmentRepository> _departmentRepositoryMock = new();
    private readonly Mock<IPositionRepository> _positionRepositoryMock = new();
    private readonly Mock<IWorkModeRepository> _workModeRepositoryMock = new();
    private readonly Mock<IEmploymentTypeRepository> _employmentTypeRepositoryMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepositoryMock = new();
    private readonly Mock<IChecklistTemplateRepository> _checklistTemplateRepositoryMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly BulkOnboardingBatch _batch;
    private readonly BulkOnboardingRowValidator _validator;

    public BulkOnboardingRowValidatorTests()
    {
        _batch = new BulkOnboardingBatch
        {
            LegalEntityId = Guid.NewGuid(),
            DefaultEmploymentType = "full_time",
            DefaultWorkModeId = 1,
        };
        _employeeRepositoryMock
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employeeRepositoryMock
            .Setup(r => r.EmployeeNumberExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employmentTypeRepositoryMock
            .Setup(r => r.GetIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _departmentRepositoryMock
            .Setup(r => r.ListByLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>());
        _positionRepositoryMock
            .Setup(r => r.ListByLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());
        _workModeRepositoryMock
            .Setup(r => r.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _checklistTemplateRepositoryMock
            .Setup(r => r.ListOnboardingMatchesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _validator = new BulkOnboardingRowValidator(
            _departmentRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _workModeRepositoryMock.Object,
            _employmentTypeRepositoryMock.Object,
            _employeeRepositoryMock.Object,
            _checklistTemplateRepositoryMock.Object);
    }

    [Fact]
    public async Task ValidateRowAsync_MissingWorkEmail_ReturnsInvalidWithReason()
    {
        var raw = new Dictionary<string, string> { ["First Name"] = "Jane", ["Last Name"] = "Doe" };
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["lastName"] = "Last Name",
            ["workEmail"] = null,
        };

        var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, new HashSet<string>(), CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Contains("email", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateRowAsync_DepartmentNameNotFound_ReturnsInvalidPointingAtOrgSettings()
    {
        _departmentRepositoryMock.Setup(r => r.ListByLegalEntityAsync(_tenantId, _batch.LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>());
        var raw = new Dictionary<string, string>
        {
            ["First Name"] = "Jane",
            ["Last Name"] = "Doe",
            ["Email"] = "jane@acme.com",
            ["Start"] = "2026-09-01",
            ["Dept"] = "Ghost Dept",
        };
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["lastName"] = "Last Name",
            ["workEmail"] = "Email",
            ["startDate"] = "Start",
            ["department"] = "Dept",
        };

        var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, new HashSet<string>(), CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Contains("Ghost Dept", outcome.ErrorMessage);
        Assert.Contains("Organization", outcome.ErrorMessage);
    }

    [Fact]
    public async Task ValidateRowAsync_DuplicateEmailWithinFile_ReturnsInvalid()
    {
        var raw = new Dictionary<string, string>
        {
            ["First Name"] = "Jane",
            ["Last Name"] = "Doe",
            ["Email"] = "jane@acme.com",
            ["Start"] = "2026-09-01",
        };
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["lastName"] = "Last Name",
            ["workEmail"] = "Email",
            ["startDate"] = "Start",
        };
        var seen = new HashSet<string> { "jane@acme.com" };

        var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, seen, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Contains("duplicate", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateRowAsync_AllFieldsResolve_ReturnsValidWithResolvedIds()
    {
        var department = new Department { Id = Guid.NewGuid(), Name = "Engineering", TenantId = _tenantId };
        _departmentRepositoryMock.Setup(r => r.ListByLegalEntityAsync(_tenantId, _batch.LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department> { department });
        _employeeRepositoryMock.Setup(r => r.EmployeeExistsInLegalEntityAsync(_tenantId, _batch.LegalEntityId, "jane@acme.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employmentTypeRepositoryMock.Setup(r => r.GetIdByCodeAsync("full_time", It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var raw = new Dictionary<string, string>
        {
            ["First Name"] = "Jane",
            ["Last Name"] = "Doe",
            ["Email"] = "jane@acme.com",
            ["Start"] = "2026-09-01",
            ["Type"] = "full_time",
            ["Dept"] = "Engineering",
        };
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["lastName"] = "Last Name",
            ["workEmail"] = "Email",
            ["startDate"] = "Start",
            ["employmentType"] = "Type",
            ["department"] = "Dept",
        };

        var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, new HashSet<string>(), CancellationToken.None);

        Assert.True(outcome.IsValid);
        Assert.Equal(department.Id, outcome.DepartmentId);
    }
}
