using System.Reflection;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Feature-specific guards for EMPLOYEE_AUTHORITY_RESOLVER_BACKEND_PART0, in addition to the
/// generic layering rules in LayerDependencyTests (which already prove the resolver's assembly
/// - ONEVO.Application - does not reference EntityFrameworkCore or any DbContext type).
/// </summary>
public class EmployeeAuthorityResolverArchitectureTests
{
    [Fact] // 29. Application interface does not depend on EF Core.
    public void IEmployeeAuthorityResolver_LivesInApplicationAssembly_NotInfrastructure()
    {
        var interfaceAssembly = typeof(IEmployeeAuthorityResolver).Assembly;
        Assert.Equal("ONEVO.Application", interfaceAssembly.GetName().Name);

        var referencedAssemblyNames = interfaceAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", referencedAssemblyNames);
        Assert.DoesNotContain("ONEVO.Infrastructure", referencedAssemblyNames);
    }

    [Fact] // 30. Resolver does not accept tenantId from request body/controller payload.
    public void ResolverRequestModels_HaveNoTenantIdProperty()
    {
        var requestTypes = new[]
        {
            typeof(EmployeeAuthorityVisibilityRequest),
            typeof(EmployeeApprovalRouteRequest),
        };

        foreach (var requestType in requestTypes)
        {
            var propertyNames = requestType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToList();

            Assert.DoesNotContain("TenantId", propertyNames);
        }
    }

    [Fact] // 32. Any new infrastructure implementation is registered in DI.
    public void EmployeeAuthorityResolver_IsRegisteredInApplicationDependencyInjection()
    {
        var path = FindFileUnderRepoRoot("src", "ONEVO.Application", "DependencyInjection.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("IEmployeeAuthorityResolver", text);
        Assert.Contains("EmployeeAuthorityResolver", text);
        Assert.Contains("AddScoped", text);
    }

    [Fact] // 33. No permission string is invented - the resolver only ever compares against
           // RequiredPermission supplied by the caller, never a hardcoded "resource:action" literal.
    public void EmployeeAuthorityResolver_NeverHardcodesAPermissionCode()
    {
        var path = FindFileUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "CoreHr", "EmployeeAuthority",
            "Services", "EmployeeAuthorityResolver.cs");
        var text = File.ReadAllText(path);

        // Permission codes in this codebase are always "resource:action" string literals
        // (see PermissionSeeder.cs). The resolver must only ever read request.RequiredPermission,
        // never author its own permission string.
        Assert.DoesNotMatch("\"[a-z_]+:[a-z_]+\"", text);
    }

    private static string FindFileUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
