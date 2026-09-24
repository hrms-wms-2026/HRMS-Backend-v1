using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetOnboardingChecklistTemplateMatches;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using Xunit;
using ChecklistTemplateEntity = ONEVO.Domain.Features.CoreHr.Entities.ChecklistTemplate;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class GetOnboardingChecklistTemplateMatchesQueryHandlerTests
{
    private readonly Mock<IChecklistTemplateRepository> _templateRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsMatchesInRepositoryOrder_WithMatchLevelLabels()
    {
        _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);
        var legalEntityId = Guid.NewGuid(); var departmentId = Guid.NewGuid(); var positionId = Guid.NewGuid();
        var positionTemplate = new ChecklistTemplateEntity { Id = Guid.NewGuid(), Name = "Position", TemplateType = "onboarding", TasksJson = "[]" };
        var companyTemplate = new ChecklistTemplateEntity { Id = Guid.NewGuid(), Name = "Company", TemplateType = "onboarding", TasksJson = "[]" };
        _templateRepository
            .Setup(r => r.ListOnboardingMatchesAsync(_tenantId, legalEntityId, departmentId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChecklistTemplateMatch> { new(positionTemplate, "position"), new(companyTemplate, "company") });
        var handler = new GetOnboardingChecklistTemplateMatchesQueryHandler(_templateRepository.Object, _currentUser.Object);

        var result = await handler.Handle(new GetOnboardingChecklistTemplateMatchesQuery(legalEntityId, departmentId, positionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(m => (m.Id, m.MatchLevel)).Should().Equal((positionTemplate.Id, "position"), (companyTemplate.Id, "company"));
    }
}
