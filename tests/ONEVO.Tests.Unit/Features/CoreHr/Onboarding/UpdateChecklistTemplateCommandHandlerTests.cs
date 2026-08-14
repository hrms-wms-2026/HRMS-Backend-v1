using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.UpdateChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using ChecklistTemplateEntity = ONEVO.Domain.Features.CoreHr.Entities.ChecklistTemplate;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class UpdateChecklistTemplateCommandHandlerTests
{
    private readonly Mock<IChecklistTemplateRepository> _templateRepository = new();
    private readonly Mock<IDepartmentRepository> _departmentRepository = new();
    private readonly Mock<IPositionRepository> _positionRepository = new();
    private readonly Mock<IChecklistTemplateAssigneeResolver> _assigneeResolver = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private UpdateChecklistTemplateCommandHandler CreateSut()
    {
        _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);
        return new UpdateChecklistTemplateCommandHandler(
            _templateRepository.Object, _departmentRepository.Object, _positionRepository.Object,
            new ChecklistTemplateTaskInputResolver(_assigneeResolver.Object), _currentUser.Object);
    }

    [Fact]
    public async Task Handle_UnknownTemplate_ReturnsNotFound()
    {
        _templateRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChecklistTemplateEntity?)null);
        var command = new UpdateChecklistTemplateCommand(Guid.NewGuid(), "New name", null, null, new List<UpdateChecklistTemplateTaskInput>());

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ValidUpdate_MutatesTrackedEntity_AndDoesNotCallAnyUpdateMethod()
    {
        var templateId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var existing = new ChecklistTemplateEntity { Id = templateId, TenantId = _tenantId, Name = "Old", TemplateType = "onboarding", LegalEntityId = legalEntityId, TasksJson = "[]", IsActive = true };
        _templateRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, templateId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var command = new UpdateChecklistTemplateCommand(
            templateId, "New name", null, null,
            new List<UpdateChecklistTemplateTaskInput> { new("Task", "employee", null, null, 2, 1, false) });

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Name.Should().Be("New name");
        // IChecklistTemplateRepository intentionally exposes no Update()/blind-overwrite method -
        // GetTrackedByIdAsync + direct mutation + SaveChangesAsync is the only path.
        _templateRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
