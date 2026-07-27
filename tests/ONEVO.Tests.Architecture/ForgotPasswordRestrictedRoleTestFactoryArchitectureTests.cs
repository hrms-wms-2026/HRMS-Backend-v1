using FluentAssertions;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards BaseForgotPasswordRestrictedRoleTestFactory, the WebApplicationFactory
/// BaseForgotPasswordRestrictedRoleHttpIntegrationTests uses to prove
/// POST /api/v1/auth/forgot-password enforces RLS over real HTTP under the restricted onevo_app
/// runtime role. Every other WebApplicationFactory in this suite (BaseDomainLoginTestFactory,
/// E2ETestFactory) strips out AddInfrastructure's ApplicationDbContext registration in
/// ConfigureServices and rebinds it directly to whatever connection string the test passes in -
/// without TenantRlsInterceptor - which makes RLS invisible to any HTTP test built on them (see
/// BaseForgotPasswordRlsIntegrationTests' own doc comment and
/// FORGOT_PASSWORD_DELIVERY_HARDENING_REPORT.md's correction). This factory is deliberately built
/// differently: it must leave Program.cs's own AddInfrastructure(...) wiring completely untouched
/// (real TenantRlsInterceptor, real onevo_app connection string supplied via process environment
/// variables) so the HTTP path is an actual proof of RLS enforcement, not just of routing.
/// </summary>
public sealed class ForgotPasswordRestrictedRoleTestFactoryArchitectureTests
{
    [Fact]
    public void Factory_NeverOverridesApplicationDbContextRegistration()
    {
        var source = ReadSource("BaseForgotPasswordRestrictedRoleTestFactory.cs");

        var forbidden = new[]
        {
            ".ConfigureServices(",
            "DbContextOptions<ApplicationDbContext>",
            "AddDbContext<ApplicationDbContext>",
            "RemoveAll",
            "services.Remove("
        };

        foreach (var term in forbidden)
        {
            source.Should().NotContain(term,
                $"the factory must leave AddInfrastructure's ApplicationDbContext + TenantRlsInterceptor " +
                $"registration completely untouched, not strip and rebind it like BaseDomainLoginTestFactory/" +
                $"E2ETestFactory do (found forbidden term: {term})");
        }
    }

    [Fact]
    public void Factory_NeverReferencesAdminModeOrRlsDisable()
    {
        var source = ReadSource("BaseForgotPasswordRestrictedRoleTestFactory.cs");

        foreach (var forbidden in new[] { "SetAdminMode", "BYPASSRLS", "DISABLE ROW LEVEL SECURITY" })
        {
            source.Should().NotContain(forbidden,
                $"the restricted-role HTTP factory must never work around RLS via {forbidden}");
        }
    }

    [Fact]
    public void Factory_DocumentsThatCallersMustSupplyTheOnevoAppConnectionString()
    {
        var source = ReadSource("BaseForgotPasswordRestrictedRoleTestFactory.cs");

        source.Should().Contain("onevo_app",
            "the factory's doc comment must make explicit that callers must supply the onevo_app " +
            "connection string (e.g. IntegrationTestEnvironmentScope.DefaultConnectionString), never " +
            "the Testcontainers superuser one");
    }

    private static string ReadSource(string fileName) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tests", "ONEVO.Tests.Integration", "Auth", fileName));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
