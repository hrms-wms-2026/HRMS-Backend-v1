using System.Reflection;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for the Release 1 Platform Admin Support Center MVP
/// (support_tickets / support_ticket_comments / platform_announcements).
/// </summary>
public class SupportCenterArchitectureTests
{
    private const string FeatureNamespace = "ONEVO.Application.Features.DevPlatform.Support";

    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    [Fact]
    public void PlatformPermissionCatalog_IncludesSupportReadAndManage()
    {
        Assert.Equal(
            "platform.support.read",
            ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers.PlatformPermissionCatalog.SupportRead);
        Assert.Equal(
            "platform.support.manage",
            ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers.PlatformPermissionCatalog.SupportManage);

        var all = ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers.PlatformPermissionCatalog.GetAll();
        Assert.Contains(all, p => p.Code == "platform.support.read");
        Assert.Contains(all, p => p.Code == "platform.support.manage");
    }

    [Fact]
    public void ApplicationFeature_DoesNotReference_EfCoreOrApplicationDbContext()
    {
        var featureTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith(FeatureNamespace))
            .ToList();
        Assert.NotEmpty(featureTypes);

        foreach (var type in featureTypes)
        {
            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    var ns = param.ParameterType.Namespace ?? string.Empty;
                    Assert.False(ns.StartsWith("Microsoft.EntityFrameworkCore"),
                        $"{type.FullName} takes an EF Core dependency: {param.ParameterType.FullName}");
                    Assert.NotEqual("ApplicationDbContext", param.ParameterType.Name);
                }
            }
        }
    }

    [Fact]
    public void SupportTicket_HasExpectedShape()
    {
        var type = typeof(ONEVO.Domain.Features.DevPlatform.Support.Entities.SupportTicket);

        var expectedProperties = new[]
        {
            "Id", "TenantId", "Subject", "Description", "Status", "Priority", "Category",
            "CreatedByPlatformUserId", "AssignedToPlatformUserId", "CreatedAt", "UpdatedAt", "ResolvedAt",
        };
        foreach (var name in expectedProperties)
        {
            Assert.NotNull(type.GetProperty(name));
        }
    }

    [Fact]
    public void SupportTicketComment_HasExpectedShape()
    {
        var type = typeof(ONEVO.Domain.Features.DevPlatform.Support.Entities.SupportTicketComment);

        var expectedProperties = new[] { "Id", "TicketId", "AuthorPlatformUserId", "Body", "IsInternal", "CreatedAt" };
        foreach (var name in expectedProperties)
        {
            Assert.NotNull(type.GetProperty(name));
        }
    }

    [Fact]
    public void PlatformAnnouncement_HasExpectedShape()
    {
        var type = typeof(ONEVO.Domain.Features.DevPlatform.Support.Entities.PlatformAnnouncement);

        var expectedProperties = new[]
        {
            "Id", "Title", "Body", "Severity", "Audience", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt",
        };
        foreach (var name in expectedProperties)
        {
            Assert.NotNull(type.GetProperty(name));
        }
    }

    [Fact]
    public void EfConfigurations_MapExpectedTableNames()
    {
        var supportTicketConfig = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations", "DevPlatform", "Support",
            "SupportTicketConfiguration.cs"));
        Assert.Contains("ToTable(\"support_tickets\")", supportTicketConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(t => t.TenantId)", supportTicketConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(t => t.Status)", supportTicketConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(t => t.Priority)", supportTicketConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(t => t.CreatedAt)", supportTicketConfig, StringComparison.Ordinal);

        var commentConfig = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations", "DevPlatform", "Support",
            "SupportTicketCommentConfiguration.cs"));
        Assert.Contains("ToTable(\"support_ticket_comments\")", commentConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(c => c.TicketId)", commentConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(c => c.CreatedAt)", commentConfig, StringComparison.Ordinal);

        var announcementConfig = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations", "DevPlatform", "Support",
            "PlatformAnnouncementConfiguration.cs"));
        Assert.Contains("ToTable(\"platform_announcements\")", announcementConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(a => a.IsPublished)", announcementConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(a => a.Severity)", announcementConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(a => a.CreatedAt)", announcementConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_CreatesAllThreeSupportCenterTablesWithRequiredIndexes()
    {
        var migrationsDir = FindRepositoryPath("src", "ONEVO.Infrastructure", "Migrations");
        var migrationFile = Directory.EnumerateFiles(migrationsDir, "*_AddSupportCenterTables.cs")
            .FirstOrDefault(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        Assert.True(migrationFile is not null,
            "Expected a migration file matching *_AddSupportCenterTables.cs under src/ONEVO.Infrastructure/Migrations.");

        var text = File.ReadAllText(migrationFile!);

        Assert.Contains("\"support_tickets\"", text, StringComparison.Ordinal);
        Assert.Contains("\"support_ticket_comments\"", text, StringComparison.Ordinal);
        Assert.Contains("\"platform_announcements\"", text, StringComparison.Ordinal);

        var requiredIndexNames = new[]
        {
            "ix_support_tickets_tenant_id",
            "ix_support_tickets_status",
            "ix_support_tickets_priority",
            "ix_support_tickets_created_at",
            "ix_support_ticket_comments_ticket_id",
            "ix_support_ticket_comments_created_at",
            "ix_platform_announcements_is_published",
            "ix_platform_announcements_severity",
            "ix_platform_announcements_created_at",
        };
        foreach (var indexName in requiredIndexNames)
        {
            Assert.Contains($"\"{indexName}\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AdminSupportTicketsController_UsesPlatformPermissions_NotTenantRequirePermission()
    {
        AssertControllerUsesPlatformPermissionsOnly(
            typeof(ONEVO.Api.Controllers.Admin.DevPlatform.Support.AdminSupportTicketsController));
    }

    [Fact]
    public void AdminPlatformAnnouncementsController_UsesPlatformPermissions_NotTenantRequirePermission()
    {
        AssertControllerUsesPlatformPermissionsOnly(
            typeof(ONEVO.Api.Controllers.Admin.DevPlatform.Support.AdminPlatformAnnouncementsController));
    }

    [Fact]
    public void EfSupportTicketRepository_HasNoExpressionBodiedMembers()
    {
        AssertNoExpressionBodiedMembers(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "DevPlatform", "Support",
            "EfSupportTicketRepository.cs"));
    }

    [Fact]
    public void EfPlatformAnnouncementRepository_HasNoExpressionBodiedMembers()
    {
        AssertNoExpressionBodiedMembers(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "DevPlatform", "Support",
            "EfPlatformAnnouncementRepository.cs"));
    }

    private static void AssertControllerUsesPlatformPermissionsOnly(Type controllerType)
    {
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var tenantAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePermissionAttribute");
            Assert.True(tenantAttr is null,
                $"{method.Name} must not use the tenant RequirePermission attribute.");

            var platformAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePlatformPermissionAttribute");
            Assert.True(platformAttr is not null,
                $"{method.Name} must be protected by RequirePlatformPermission.");
        }
    }

    private static void AssertNoExpressionBodiedMembers(string repositoryPath)
    {
        var sourceLines = File.ReadAllLines(repositoryPath);
        var expressionBodiedMembers = sourceLines
            .Select((line, index) => new { LineNumber = index + 1, Text = line.Trim() })
            .Where(line =>
                line.Text.StartsWith("=>", StringComparison.Ordinal) ||
                (line.Text.StartsWith("public ", StringComparison.Ordinal) &&
                 line.Text.Contains("=>", StringComparison.Ordinal)))
            .Select(line => $"{repositoryPath}:{line.LineNumber}: {line.Text}")
            .ToArray();

        Assert.Empty(expressionBodiedMembers);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
