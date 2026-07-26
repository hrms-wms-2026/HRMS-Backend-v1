using System.Reflection;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the retirement of active tenant_one_time_charges API/Application/Infrastructure
/// behavior documented in TENANT_ONE_TIME_CHARGES_ACTIVE_RETIREMENT_REPORT.md. OneVo-HR has
/// no canonical support for tenant_one_time_charges (paid setup pricing lives on
/// tenant_setup_services.price/is_billable instead). The tenant_one_time_charges table,
/// TenantOneTimeCharge entity, its EF configuration, and its migrations are intentionally
/// left in place (legacy/inactive schema residue) - this suite only guards against the
/// retired active code paths being reintroduced. It does not block a future canonical
/// one-time billing feature under different type/route names.
/// </summary>
public class TenantOneTimeChargeActiveRetirementArchitectureTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(ONEVO.Infrastructure.Persistence.ApplicationDbContext).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(ONEVO.Api.Filters.RequirePlatformPermissionAttribute).Assembly;

    [Fact]
    public void NoController_ExposesOneTimeChargesRoute()
    {
        var offenders = new List<string>();

        foreach (var type in ApiAssembly.GetTypes())
        {
            if (!type.Name.EndsWith("Controller", StringComparison.Ordinal))
                continue;

            var classRoutes = type.GetCustomAttributes()
                .Select(DescribeRouteAttribute)
                .Where(r => r is not null);

            var methodRoutes = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes())
                .Select(DescribeRouteAttribute)
                .Where(r => r is not null);

            foreach (var route in classRoutes.Concat(methodRoutes))
            {
                if (route!.Contains("one-time-charges", StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{type.FullName}: {route}");
            }
        }

        Assert.True(offenders.Count == 0,
            "No controller may expose a one-time-charges route (retired, unsupported by OneVo-HR): " +
            string.Join("; ", offenders));
    }

    private static string? DescribeRouteAttribute(Attribute attribute)
    {
        var attributeType = attribute.GetType();
        if (attributeType.Name is not ("RouteAttribute" or "HttpGetAttribute" or "HttpPostAttribute"
            or "HttpPatchAttribute" or "HttpPutAttribute" or "HttpDeleteAttribute"))
        {
            return null;
        }

        var templateProperty = attributeType.GetProperty("Template");
        return templateProperty?.GetValue(attribute) as string;
    }

    [Fact]
    public void Application_HasNoActiveCreateOneTimeChargeCommand()
    {
        AssertTypeDoesNotExist(ApplicationAssembly,
            "ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateOneTimeCharge.CreateOneTimeChargeCommand");
        AssertTypeDoesNotExist(ApplicationAssembly,
            "ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateOneTimeCharge.CreateOneTimeChargeCommandHandler");
    }

    [Fact]
    public void Application_HasNoActiveUpdateOneTimeChargeCommand()
    {
        AssertTypeDoesNotExist(ApplicationAssembly,
            "ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateOneTimeCharge.UpdateOneTimeChargeCommand");
        AssertTypeDoesNotExist(ApplicationAssembly,
            "ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateOneTimeCharge.UpdateOneTimeChargeCommandHandler");
    }

    [Fact]
    public void Application_HasNoActiveGetTenantOneTimeChargesQuery()
    {
        AssertTypeDoesNotExist(ApplicationAssembly,
            "ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantOneTimeCharges.GetTenantOneTimeChargesQuery");
        AssertTypeDoesNotExist(ApplicationAssembly,
            "ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantOneTimeCharges.GetTenantOneTimeChargesQueryHandler");
    }

    [Fact]
    public void NoDIRegistration_ExistsForTenantOneTimeChargeRepository()
    {
        AssertTypeDoesNotExist(ApplicationAssembly,
            "ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces.ITenantOneTimeChargeRepository");
        AssertTypeDoesNotExist(InfrastructureAssembly,
            "ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing.EfTenantOneTimeChargeRepository");

        var dependencyInjectionSource = File.ReadAllText(FindFileAboveBaseDirectory(
            Path.Combine("src", "ONEVO.Infrastructure", "DependencyInjection.cs")));

        Assert.DoesNotContain("ITenantOneTimeChargeRepository", dependencyInjectionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EfTenantOneTimeChargeRepository", dependencyInjectionSource, StringComparison.Ordinal);
    }

    private static void AssertTypeDoesNotExist(Assembly assembly, string fullyQualifiedName)
    {
        var type = assembly.GetType(fullyQualifiedName);
        Assert.True(type is null,
            $"{fullyQualifiedName} is retired active behavior; tenant_one_time_charges has no canonical OneVo-HR support.");
    }

    private static string FindFileAboveBaseDirectory(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} above {AppContext.BaseDirectory}");
    }
}
