using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Compliance;

using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class EfLegalDocumentVersionRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetCurrentRequiredVersionsAsync_ExcludesNonDashboardBlockScope()
    {
        await using var db = BuildInMemoryDb();

        db.LegalDocumentVersions.AddRange(
            BuildVersion("terms", "dashboard"),
            BuildVersion("workpulse_consent", "workpulse_collection"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var result = await repository.GetCurrentRequiredVersionsAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].DocumentType.Should().Be("terms");
        result[0].BlockScope.Should().Be("dashboard");
    }

    [Fact]
    public async Task GetCurrentRequiredVersionsAsync_ExcludesNonRequiredAndUnpublishedAndFutureRows()
    {
        await using var db = BuildInMemoryDb();

        var notRequired = BuildVersion("terms", "dashboard");
        notRequired.IsRequired = false;

        var draft = BuildVersion("privacy_notice", "dashboard");
        draft.Status = "draft";

        var future = BuildVersion("marketing", "dashboard");
        future.PublishedAt = Now.AddDays(1);

        var eligible = BuildVersion("biometric_photo_consent", "dashboard");

        db.LegalDocumentVersions.AddRange(notRequired, draft, future, eligible);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var result = await repository.GetCurrentRequiredVersionsAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].DocumentType.Should().Be("biometric_photo_consent");
    }

    private static LegalDocumentVersion BuildVersion(string documentType, string blockScope)
    {
        return new LegalDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentType = documentType,
            Version = "1.0",
            Title = documentType,
            IsRequired = true,
            BlockScope = blockScope,
            Status = "published",
            PublishedAt = Now.AddDays(-1)
        };
    }

    private static IDateTimeProvider BuildClock()
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        return clock.Object;
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }
}
