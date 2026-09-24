using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ArchiveChecklistTemplate;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using Xunit;
using ChecklistTemplateEntity = ONEVO.Domain.Features.CoreHr.Entities.ChecklistTemplate;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class ArchiveChecklistTemplateCommandHandlerTests
{
    private readonly Mock<IChecklistTemplateRepository> _templateRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private ArchiveChecklistTemplateCommandHandler CreateSut()
    {
        _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);
        return new ArchiveChecklistTemplateCommandHandler(_templateRepository.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_UnknownTemplate_ReturnsNotFound()
    {
        _templateRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChecklistTemplateEntity?)null);

        var result = await CreateSut().Handle(new ArchiveChecklistTemplateCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ActiveTemplate_SetsIsActiveFalse_NeverDeletes()
    {
        var template = new ChecklistTemplateEntity { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "X", TemplateType = "onboarding", TasksJson = "[]", IsActive = true };
        _templateRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var result = await CreateSut().Handle(new ArchiveChecklistTemplateCommand(template.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.IsActive.Should().BeFalse();
        _templateRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyInactiveTemplate_SucceedsIdempotently()
    {
        var template = new ChecklistTemplateEntity { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "X", TemplateType = "onboarding", TasksJson = "[]", IsActive = false };
        _templateRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var result = await CreateSut().Handle(new ArchiveChecklistTemplateCommand(template.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.IsActive.Should().BeFalse();
    }
}
