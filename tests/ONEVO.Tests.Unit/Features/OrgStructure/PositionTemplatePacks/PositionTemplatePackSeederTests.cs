using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.PositionTemplatePacks;

public sealed class PositionTemplatePackSeederTests
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

    private static async Task<PlatformUser> AddPlatformUserAsync(ApplicationDbContext db)
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "seed-op@onevo.test",
            FullName = "Seed Operator",
            Status = PlatformUser.StatusActive,
            MfaStatus = PlatformUser.MfaNotEnrolled,
            InviteStatus = PlatformUser.InviteAccepted,
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SeedAsync_WithPlatformUser_SeedsSevenActiveSystemPositionTemplates()
    {
        using var db = BuildInMemoryDb();
        await AddPlatformUserAsync(db);

        await PositionTemplatePackSeeder.SeedAsync(db, NullLogger.Instance, CancellationToken.None);

        var seeded = await db.ConfigurationTemplates
            .Where(t => t.TemplateType == ConfigurationTemplate.TypePositionTemplate)
            .ToListAsync();

        seeded.Should().HaveCount(7);
        seeded.Should().OnlyContain(t => t.IsSystem && t.IsActive);
        seeded.Should().OnlyContain(t => t.TemplateType == "position_template");
        seeded.Select(t => t.TemplateKey).Should().OnlyHaveUniqueItems();

        // Part 2's frontend references packs by templateKey - pin the exact catalog so a rename
        // or accidental drop is caught here rather than surfacing as a silent frontend contract break.
        seeded.Select(t => t.TemplateKey).Should().BeEquivalentTo(new[]
        {
            "executive-leadership-template",
            "hr-people-operations-template",
            "management-layer-template",
            "team-lead-template",
            "project-delivery-management-template",
            "software-engineering-starter-template",
            "operations-starter-template"
        });

        // The tenant-facing query handler bounds its read to 200 rows (not exposed as pagination).
        // Keep the seeded catalog well under that bound so growth is a deliberate decision, not a
        // silent truncation.
        seeded.Count.Should().BeLessThan(200);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_WhenRunTwice()
    {
        using var db = BuildInMemoryDb();
        await AddPlatformUserAsync(db);

        await PositionTemplatePackSeeder.SeedAsync(db, NullLogger.Instance, CancellationToken.None);
        var firstCount = await db.ConfigurationTemplates.CountAsync();

        await PositionTemplatePackSeeder.SeedAsync(db, NullLogger.Instance, CancellationToken.None);
        var secondCount = await db.ConfigurationTemplates.CountAsync();

        secondCount.Should().Be(firstCount);
    }

    [Fact]
    public async Task SeedAsync_WithoutAnyPlatformUser_SkipsSeeding_AndDoesNotThrow()
    {
        using var db = BuildInMemoryDb();

        var act = () => PositionTemplatePackSeeder.SeedAsync(db, NullLogger.Instance, CancellationToken.None);
        await act.Should().NotThrowAsync();

        (await db.ConfigurationTemplates.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_EachPackPayload_ParsesAsWellFormedPositionTemplatePayload()
    {
        using var db = BuildInMemoryDb();
        await AddPlatformUserAsync(db);

        await PositionTemplatePackSeeder.SeedAsync(db, NullLogger.Instance, CancellationToken.None);

        var seeded = await db.ConfigurationTemplates
            .Where(t => t.TemplateType == ConfigurationTemplate.TypePositionTemplate)
            .ToListAsync();

        foreach (var template in seeded)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(template.PayloadJson);
            var root = doc.RootElement;
            root.GetProperty("pack_name").GetString().Should().NotBeNullOrWhiteSpace();
            root.GetProperty("employee_count_range_key").GetString().Should().NotBeNullOrWhiteSpace();
            root.GetProperty("employee_count_min").GetInt32().Should().BeGreaterThan(0);
            var positions = root.GetProperty("positions");
            positions.GetArrayLength().Should().BeGreaterThan(0);

            template.ModuleKeysJson.Should().Be("""["core_hr"]""");
            template.CreatedById.Should().NotBe(Guid.Empty);
        }
    }
}
