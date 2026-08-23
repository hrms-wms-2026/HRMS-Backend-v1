using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.CreateChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class CreateChecklistTemplateCommandHandlerTests
{
    private readonly Mock<IChecklistTemplateRepository> _templateRepository = new();
    private readonly Mock<ILegalEntityRepository> _legalEntityRepository = new();
    private readonly Mock<IDepartmentRepository> _departmentRepository = new();
    private readonly Mock<IPositionRepository> _positionRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    private CreateChecklistTemplateCommandHandler CreateSut()
    {
        _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);
        return new CreateChecklistTemplateCommandHandler(
            _templateRepository.Object, _legalEntityRepository.Object, _departmentRepository.Object, _positionRepository.Object,
            new ChecklistTemplateTaskInputResolver(_positionRepository.Object), _currentUser.Object);
    }

    private void SetupActiveLegalEntity()
        => _legalEntityRepository.Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });

    [Fact]
    public async Task Handle_CompanyDefaultTemplate_Succeeds()
    {
        SetupActiveLegalEntity();
        var command = new CreateChecklistTemplateCommand(
            "Standard Onboarding", "onboarding", _legalEntityId, null, null,
            new List<CreateChecklistTemplateTaskInput> { new("Complete profile", "employee", null, null, 1, 1, true) });

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LegalEntityId.Should().Be(_legalEntityId);
        result.Value.DepartmentId.Should().BeNull();
        result.Value.PositionId.Should().BeNull();
        _templateRepository.Verify(r => r.AddAsync(It.IsAny<ONEVO.Domain.Features.CoreHr.Entities.ChecklistTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
        _templateRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InactiveLegalEntity_ReturnsUnprocessableEntity()
    {
        _legalEntityRepository.Setup(r => r.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = false });
        var command = new CreateChecklistTemplateCommand("X", "onboarding", _legalEntityId, null, null, new List<CreateChecklistTemplateTaskInput>());

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        _templateRepository.Verify(r => r.AddAsync(It.IsAny<ONEVO.Domain.Features.CoreHr.Entities.ChecklistTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PositionInDifferentDepartment_ReturnsUnprocessableEntity()
    {
        SetupActiveLegalEntity();
        var positionId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        _positionRepository.Setup(r => r.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, LegalEntityId = _legalEntityId, DepartmentId = Guid.NewGuid(), IsActive = true });
        var command = new CreateChecklistTemplateCommand("X", "onboarding", _legalEntityId, departmentId, positionId, new List<CreateChecklistTemplateTaskInput>());

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_TaskWithBothAssignedToIdAndAssigneePositionId_ReturnsUnprocessableEntity()
    {
        SetupActiveLegalEntity();
        var command = new CreateChecklistTemplateCommand(
            "X", "onboarding", _legalEntityId, null, null,
            new List<CreateChecklistTemplateTaskInput> { new("Task", "custom_user", Guid.NewGuid(), Guid.NewGuid(), 1, null, true) });

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_CustomUserTaskWithPositionOnly_PersistsWithoutAssignedToId()
    {
        SetupActiveLegalEntity();
        var positionId = Guid.NewGuid();
        _positionRepository.Setup(r => r.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, LegalEntityId = _legalEntityId, IsActive = true, TenantId = _tenantId });
        var command = new CreateChecklistTemplateCommand(
            "X", "onboarding", _legalEntityId, null, null,
            new List<CreateChecklistTemplateTaskInput> { new("IT setup", "custom_user", null, positionId, 1, 1, true) });

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Tasks.Should().ContainSingle();
        result.Value.Tasks[0].AssignedToId.Should().BeNull();
        result.Value.Tasks[0].AssigneePositionId.Should().Be(positionId);
    }
}
