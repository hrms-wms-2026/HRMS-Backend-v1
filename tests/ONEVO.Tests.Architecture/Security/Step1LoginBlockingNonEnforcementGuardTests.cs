using FluentAssertions;
using Xunit;

namespace ONEVO.Tests.Architecture.Security;

public class Step1LoginBlockingNonEnforcementGuardTests
{
    [Fact]
    public void Migration_ReconcilesGdprConsentToLegalAcceptanceAndCreatesLegalDocumentVersions()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFile = Path.Combine(
            migrationsDir,
            "20260724120000_AddLegalDocumentVersionsAndLegalAcceptanceRecords.cs");

        File.Exists(migrationFile).Should().BeTrue("Migration file must exist");

        var code = File.ReadAllText(migrationFile);

        // Verify legal_document_versions table creation
        code.Should().Contain("legal_document_versions");
        code.Should().Contain("ix_legal_document_versions_document_type_published");
        code.Should().Contain("WHERE status = 'published'");

        // Verify gdpr_consent_records rename and reconciliation
        code.Should().Contain("gdpr_consent_records");
        code.Should().Contain("legal_acceptance_records");
        code.Should().Contain("legacy_v0");
        code.Should().Contain("legacy_migration");

        // Verify RLS policy re-application on legal_acceptance_records
        code.Should().Contain("TenantTables");
        code.Should().Contain("legal_acceptance_records");
        code.Should().Contain("ENABLE ROW LEVEL SECURITY");
        code.Should().Contain("CREATE POLICY tenant_isolation ON");
    }

    [Fact]
    public void Step2_LoginHandlersNowRouteThroughLoginContinuationServiceForLegalBlocking()
    {
        // Supersedes the former Step1_LoginHandlersDoNotEnforceSessionBlockingYet guard: Step 2
        // (production-readiness pass) wires legal_login_challenges blocking into a shared
        // ILoginContinuationService, so LoginCommandHandler/BaseLoginCommandHandler/
        // SelectWorkspaceCommandHandler no longer call ILoginSessionMaterialFactory directly -
        // they all delegate through the continuation service, which owns the legal check and the
        // final PrepareAsync call.
        var srcDir = FindSrcDirectory();
        var loginHandlerFile = Path.Combine(
            srcDir,
            "ONEVO.Application",
            "Features",
            "Auth",
            "Login",
            "Commands",
            "Login",
            "LoginCommandHandler.cs");

        File.Exists(loginHandlerFile).Should().BeTrue();
        var code = File.ReadAllText(loginHandlerFile);

        code.Should().NotContain("_sessionMaterialFactory.PrepareAsync",
            "session issuance is now owned exclusively by ILoginContinuationService, not the handler");
        code.Should().Contain("ILoginContinuationService");
        code.Should().Contain("_continuation.ContinueAsync");
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }

    private static string FindSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src above " + AppContext.BaseDirectory);
    }
}
