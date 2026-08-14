using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ConfigurationTemplates;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.PositionTemplatePacks;

/// <summary>Confirms, against a real (in-memory) EF query, that the shared
/// IConfigurationTemplateRepository.ListAsync contract - reused as-is by the tenant-facing
/// position template pack read - returns only active position_template rows and excludes other
/// template types and inactive rows. Complements the mocked handler-level tests.</summary>
public sealed class EfConfigurationTemplateRepositoryPositionTemplateFilterTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new Mock<ITenantContext>().Object);
    }

    private static ConfigurationTemplate Template(string key, string type, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        TemplateKey = key,
        TemplateType = type,
        Name = key,
        ModuleKeysJson = "[]",
        PayloadJson = "{}",
        IsActive = isActive,
        CreatedById = Guid.NewGuid(),
    };

    [Fact]
    public async Task ListAsync_PositionTemplateActiveOnly_ExcludesOtherTypesAndInactiveRows()
    {
        using var db = BuildInMemoryDb();
        db.ConfigurationTemplates.AddRange(
            Template("active-position", ConfigurationTemplate.TypePositionTemplate, isActive: true),
            Template("inactive-position", ConfigurationTemplate.TypePositionTemplate, isActive: false),
            Template("active-time-off", ConfigurationTemplate.TypeTimeOffPolicy, isActive: true),
            Template("active-onboarding", ConfigurationTemplate.TypeOnboarding, isActive: true),
            Template("active-configuration", ConfigurationTemplate.TypeConfiguration, isActive: true));
        await db.SaveChangesAsync();

        var repository = new EfConfigurationTemplateRepository(db);
        var results = await repository.ListAsync(
            ConfigurationTemplate.TypePositionTemplate, activeOnly: true, industryProfileTag: null,
            skip: 0, take: 200, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].TemplateKey.Should().Be("active-position");
    }
}
