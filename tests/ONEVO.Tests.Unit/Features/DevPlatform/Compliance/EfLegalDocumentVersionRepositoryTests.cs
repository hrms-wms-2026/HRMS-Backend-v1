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

    [Fact]
    public async Task ListAsync_FiltersByDocumentTypeAndStatus()
    {
        await using var db = BuildInMemoryDb();

        var draftTerms = BuildVersion("terms", "dashboard");
        draftTerms.Status = "draft";
        var publishedTerms = BuildVersion("terms", "dashboard");
        var publishedPrivacy = BuildVersion("privacy_notice", "dashboard");

        db.LegalDocumentVersions.AddRange(draftTerms, publishedTerms, publishedPrivacy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var result = await repository.ListAsync("terms", "published", CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(publishedTerms.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTrackedEntity_ForMutation()
    {
        await using var db = BuildInMemoryDb();

        var version = BuildVersion("terms", "dashboard");
        db.LegalDocumentVersions.Add(version);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var found = await repository.GetByIdAsync(version.Id, CancellationToken.None);
        found.Should().NotBeNull();
        found!.Title = "Changed";
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var reloaded = await db.LegalDocumentVersions.FindAsync(version.Id);
        reloaded!.Title.Should().Be("Changed");
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("archived")]
    public async Task GetPublishedAsync_ReturnsNull_WhenStatusIsNotPublished(string status)
    {
        await using var db = BuildInMemoryDb();

        var version = BuildVersion("terms", "dashboard");
        version.Status = status;
        db.LegalDocumentVersions.Add(version);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var found = await repository.GetPublishedAsync("terms", "1.0", CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentPublishedByDocumentTypeAsync_ReturnsPublishedRow()
    {
        await using var db = BuildInMemoryDb();

        var published = BuildVersion("terms", "dashboard");
        db.LegalDocumentVersions.Add(published);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var found = await repository.GetCurrentPublishedByDocumentTypeAsync("terms", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(published.Id);
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
