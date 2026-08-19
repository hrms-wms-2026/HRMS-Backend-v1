using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class OffboardingChecklistTemplateMatchTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        var currentUser = new Mock<ICurrentUser>();
        var publisher = new Mock<IPublisher>();
        var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenant.Object);
    }

    [Fact]
    public async Task ListOffboardingMatchesAsync_PrefersPositionMatch_ExcludesOnboardingTemplates()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        db.ChecklistTemplates.AddRange(
            new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Company Default", TemplateType = "offboarding", LegalEntityId = legalEntityId, IsActive = true, TasksJson = "[]" },
            new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenantId, Name = "High-Risk Exit", TemplateType = "offboarding", LegalEntityId = legalEntityId, PositionId = positionId, IsActive = true, TasksJson = "[]" },
            new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Onboarding Default", TemplateType = "onboarding", LegalEntityId = legalEntityId, IsActive = true, TasksJson = "[]" });
        await db.SaveChangesAsync();

        var repo = new EfChecklistTemplateRepository(db);
        var matches = await repo.ListOffboardingMatchesAsync(tenantId, legalEntityId, departmentId: null, positionId: positionId);

        matches.Should().HaveCount(2);
        matches[0].Template.Name.Should().Be("High-Risk Exit");
        matches[0].MatchLevel.Should().Be(ChecklistTemplateMatchLevels.Position);
        matches.Should().NotContain(m => m.Template.Name == "Onboarding Default");
    }
}
