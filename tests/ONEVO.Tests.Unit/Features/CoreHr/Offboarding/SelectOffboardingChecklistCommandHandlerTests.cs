using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class SelectOffboardingChecklistCommandHandlerTests
{
    private readonly Mock<IOffboardingRecordRepository> _offboardingRecordRepository = new();
    private readonly Mock<IChecklistTemplateRepository> _checklistTemplateRepository = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _employeeChecklistTaskRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _templateId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public SelectOffboardingChecklistCommandHandlerTests()
    {
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    private SelectOffboardingChecklistCommandHandler CreateSut() => new(
        _offboardingRecordRepository.Object, _checklistTemplateRepository.Object,
        _employeeChecklistTaskRepository.Object, _employeeRepository.Object, _currentUser.Object, _clock.Object);

    [Fact]
    public async Task Handle_NoOpenRecord_ReturnsNotFound()
    {
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OffboardingRecord?)null);

        var result = await CreateSut().Handle(new SelectOffboardingChecklistCommand(_employeeId, _templateId), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ChecklistAlreadySelected_ReturnsConflict()
    {
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress });

        var result = await CreateSut().Handle(new SelectOffboardingChecklistCommand(_employeeId, _templateId), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_InstantiatesTasksAndAdvancesStatus()
    {
        var record = new OffboardingRecord { Id = Guid.NewGuid(), EmployeeId = _employeeId, Status = OffboardingRecordStatuses.Initiated, LastWorkingDate = new DateOnly(2026, 12, 1) };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _checklistTemplateRepository.Setup(r => r.GetByIdAsync(_tenantId, _templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChecklistTemplate { Id = _templateId, TenantId = _tenantId, TemplateType = "offboarding", IsActive = true, LegalEntityId = _legalEntityId, TasksJson = "[]" });
        var employee = new EmployeeEntity { Id = _employeeId, LegalEntityId = _legalEntityId, UserId = Guid.NewGuid() };
        _employeeRepository.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var instantiatedTasks = new List<EmployeeChecklistTask> { new() { Id = Guid.NewGuid(), TenantId = _tenantId } };
        _employeeChecklistTaskRepository
            .Setup(r => r.InstantiateAsync(It.IsAny<ChecklistTemplate>(), _employeeId, employee.UserId, null, record.LastWorkingDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instantiatedTasks);

        var result = await CreateSut().Handle(new SelectOffboardingChecklistCommand(_employeeId, _templateId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instantiatedTasks[0].OffboardingRecordId.Should().Be(record.Id);
        record.Status.Should().Be(OffboardingRecordStatuses.InProgress);
        record.ChecklistTemplateId.Should().Be(_templateId);
        _offboardingRecordRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
