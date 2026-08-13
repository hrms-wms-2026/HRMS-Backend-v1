using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistTemplates;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using Xunit;
using ChecklistTemplateEntity = ONEVO.Domain.Features.CoreHr.Entities.ChecklistTemplate;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class GetAndListChecklistTemplateQueryHandlerTests
{
    private readonly Mock<IChecklistTemplateRepository> _templateRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public GetAndListChecklistTemplateQueryHandlerTests() => _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);

    [Fact]
    public async Task GetById_UnknownTemplate_ReturnsNotFound()
    {
        _templateRepository.Setup(r => r.GetByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChecklistTemplateEntity?)null);
        var handler = new GetChecklistTemplateByIdQueryHandler(_templateRepository.Object, _currentUser.Object);

        var result = await handler.Handle(new GetChecklistTemplateByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_ExistingTemplate_ReturnsParsedTasks()
    {
        var template = new ChecklistTemplateEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Starter", TemplateType = "onboarding", LegalEntityId = Guid.NewGuid(), IsActive = true,
            TasksJson = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueOffsetDays\":1,\"isRequired\":true}]",
        };
        _templateRepository.Setup(r => r.GetByIdAsync(_tenantId, template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        var handler = new GetChecklistTemplateByIdQueryHandler(_templateRepository.Object, _currentUser.Object);

        var result = await handler.Handle(new GetChecklistTemplateByIdQuery(template.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Tasks.Should().ContainSingle(t => t.Title == "Complete profile" && t.AssignedToId == null);
    }

    [Fact]
    public async Task List_MapsItemsAndTotalCount()
    {
        var legalEntityId = Guid.NewGuid();
        var template = new ChecklistTemplateEntity { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "A", TemplateType = "onboarding", LegalEntityId = legalEntityId, TasksJson = "[]", IsActive = true };
        _templateRepository
            .Setup(r => r.ListAsync(_tenantId, It.Is<ChecklistTemplateListFilter>(f => f.LegalEntityId == legalEntityId), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ChecklistTemplateEntity> { template }, 1));
        var handler = new ListChecklistTemplatesQueryHandler(_templateRepository.Object, _currentUser.Object);

        var result = await handler.Handle(new ListChecklistTemplatesQuery(null, legalEntityId, null, null, false, 1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(i => i.Id == template.Id);
    }
}
