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
    public void Step2_LoginHandlersRouteThroughLoginContinuationServiceForLegalBlocking()
    {
        // Supersedes the former Step1_LoginHandlersDoNotEnforceSessionBlockingYet guard, then later
        // retargeted off the retired tenant-host LoginCommandHandler (deleted along with direct
        // tenant-host password login - see TENANT_HOST_PASSWORD_LOGIN_RETIREMENT_REPORT.md).
        // BaseLoginCommandHandler/SelectWorkspaceCommandHandler must not call
        // ILoginSessionMaterialFactory directly - they delegate through the continuation service,
        // which owns the legal check and the final PrepareAsync call.
        var srcDir = FindSrcDirectory();
        var handlerFiles = new[]
        {
            Path.Combine(srcDir, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseLogin", "BaseLoginCommandHandler.cs"),
            Path.Combine(srcDir, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "SelectWorkspace", "SelectWorkspaceCommandHandler.cs")
        };

        foreach (var handlerFile in handlerFiles)
        {
            File.Exists(handlerFile).Should().BeTrue();
            var code = File.ReadAllText(handlerFile);

            code.Should().NotContain("_sessionMaterialFactory.PrepareAsync",
                $"{Path.GetFileName(handlerFile)}: session issuance is owned exclusively by ILoginContinuationService");
            code.Should().Contain("ILoginContinuationService");
            code.Should().Contain("_continuation.ContinueAsync");
        }

        var deletedLoginCommandHandler = Path.Combine(
            srcDir, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "Login", "LoginCommandHandler.cs");
        File.Exists(deletedLoginCommandHandler).Should().BeFalse(
            "the tenant-host password-login LoginCommandHandler was retired and must not be recreated");
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
