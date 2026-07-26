using FluentAssertions;

namespace ONEVO.Tests.Architecture;

public sealed class BaseLoginArchitectureTests
{
    [Fact]
    public void EfBaseLoginCandidateRepository_QueriesOnlyTheAllowlistedFunction()
    {
        var sourcePath = FindSourceFile(
            "ONEVO.Infrastructure",
            "Persistence", "Repositories", "Auth", "Login", "EfBaseLoginCandidateRepository.cs");

        var text = File.ReadAllText(sourcePath);

        text.Should().Contain("auth_lookup_base_login_candidates",
            "the repository must call the allowlisted function");
        text.Should().NotContain("_db.Users", "base-login candidate lookup must not query the Users DbSet directly");
        text.Should().NotContain("_db.Tenants", "base-login candidate lookup must not query the Tenants DbSet directly");
        text.Should().NotContain("IgnoreQueryFilters",
            "base-login candidate lookup must not bypass tenant filters directly; it must use the function instead");
    }

    [Fact]
    public void FunctionOwnerRoleGrants_AreColumnLevelOnly_NotBroadTableSelect()
    {
        var migrationPath = FindLatestNormalizedEmailMigration();
        var bootstrapPath = Path.Combine(FindRepositoryRoot(), "ops", "postgres", "local-bootstrap-roles.sql");
        File.Exists(bootstrapPath).Should().BeTrue($"expected {bootstrapPath} to exist");

        var migrationText = File.ReadAllText(migrationPath);
        var bootstrapText = File.ReadAllText(bootstrapPath);

        var broadGrantPattern = new System.Text.RegularExpressions.Regex(
            @"GRANT\s+SELECT\s+ON\s+users\s*,\s*tenants", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        broadGrantPattern.IsMatch(migrationText).Should().BeFalse(
            "the migration must not grant broad table-level SELECT on users/tenants");
        broadGrantPattern.IsMatch(bootstrapText).Should().BeFalse(
            "local-bootstrap-roles.sql must not grant broad table-level SELECT on users/tenants");

        const string usersColumnGrant =
            "GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash) ON public.users";
        const string tenantsColumnGrant = "GRANT SELECT (id, slug, name, status) ON public.tenants";

        migrationText.Should().Contain(usersColumnGrant,
            "the normalized-email migration must grant column-level SELECT including normalized_email, not email");
        bootstrapText.Should().Contain(usersColumnGrant,
            "local-bootstrap-roles.sql must grant column-level SELECT including normalized_email, not email");
        bootstrapText.Should().Contain(tenantsColumnGrant,
            "local-bootstrap-roles.sql must grant column-level SELECT on exactly the tenants columns the function needs");
    }

    private static string FindLatestNormalizedEmailMigration()
    {
        var migrationsDir = Path.Combine(FindRepositoryRoot(), "src", "ONEVO.Infrastructure", "Migrations");
        var match = Directory.EnumerateFiles(migrationsDir, "*_AddUsersNormalizedEmail.cs").SingleOrDefault();
        match.Should().NotBeNull("expected a single AddUsersNormalizedEmail migration file");
        return match!;
    }

    [Fact]
    public void ProductionSource_ContainsNoFindWorkspaceRoute()
    {
        var offenders = ScanProductionSourceFor("find-workspace");
        offenders.Should().BeEmpty("the unsafe email-only workspace-discovery pattern is explicitly prohibited");
    }

    [Fact]
    public void ProductionSource_ContainsNoWorkspaceSlugField()
    {
        var offenders = ScanProductionSourceFor("workspaceSlug");
        offenders.Should().BeEmpty("the browser must never be able to supply a tenant hint via workspaceSlug");
    }

    [Fact]
    public void BaseLoginAndSelectWorkspaceSource_ContainsNoXTenantIdUsage()
    {
        var baseLoginHandlerPath = FindSourceFile(
            "ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseLogin", "BaseLoginCommandHandler.cs");
        var selectWorkspaceHandlerPath = FindSourceFile(
            "ONEVO.Application", "Features", "Auth", "Login", "Commands", "SelectWorkspace", "SelectWorkspaceCommandHandler.cs");
        var controllerPath = FindSourceFile(
            "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthLoginController.cs");

        foreach (var path in new[] { baseLoginHandlerPath, selectWorkspaceHandlerPath, controllerPath })
        {
            File.ReadAllText(path).Should().NotContain("X-Tenant-Id",
                $"{Path.GetFileName(path)} must never read a browser-supplied tenant hint");
        }
    }

    [Fact]
    public void BrowserFacingLoginResponseTypes_ContainNoTokenOrJwtFields()
    {
        // BaseLoginWorkspaceOptionDto/BaseLoginWorkspaceSelectionRequiredDto are handler-layer
        // (never sent to the browser directly - AuthController maps them into
        // WorkspaceOptionResponse/WorkspaceSelectionRequiredResponse first), but are included here
        // too as extra insurance since they feed those wire types.
        var typesToCheck = new[]
        {
            typeof(ONEVO.Application.Features.Auth.Login.DTOs.Responses.AuthSessionResponseDto),
            typeof(ONEVO.Api.Contracts.Auth.WorkspaceSelectionRequiredResponse),
            typeof(ONEVO.Api.Contracts.Auth.WorkspaceOptionResponse),
            typeof(ONEVO.Application.Features.Auth.Login.DTOs.Responses.BaseLoginWorkspaceOptionDto),
            typeof(ONEVO.Application.Features.Auth.Login.DTOs.Responses.BaseLoginWorkspaceSelectionRequiredDto)
        };

        var forbiddenSubstrings = new[] { "token", "jwt", "bearer", "tenantid", "userid", "passwordhash" };

        foreach (var type in typesToCheck)
        {
            var propertyNames = type.GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
            foreach (var forbidden in forbiddenSubstrings)
            {
                propertyNames.Should().NotContain(
                    name => name.Contains(forbidden),
                    $"{type.Name} is serialized (directly or via mapping) to the browser and must never carry a {forbidden}-shaped field");
            }
        }
    }

    [Fact]
    public void WorkspaceOptionResponse_ExposesOnlySlugAndDisplayName()
    {
        var properties = typeof(ONEVO.Api.Contracts.Auth.WorkspaceOptionResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        properties.Should().BeEquivalentTo(new[] { "Slug", "DisplayName" },
            "the multi-match response must expose only safe workspace display fields, never tenant/user IDs");
    }

    [Fact]
    public void WorkspaceSelectionRequiredResponse_ExposesOnlyExpectedFields()
    {
        var properties = typeof(ONEVO.Api.Contracts.Auth.WorkspaceSelectionRequiredResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        properties.Should().BeEquivalentTo(
            new[] { "RequiresWorkspaceSelection", "LoginChallenge", "Workspaces" },
            "the multi-match envelope must expose only requires_workspace_selection/login_challenge/workspaces");
    }

    [Fact]
    public void BaseLoginWorkspaceOptionDto_ExposesOnlySlugAndDisplayName()
    {
        var properties = typeof(ONEVO.Application.Features.Auth.Login.DTOs.Responses.BaseLoginWorkspaceOptionDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        properties.Should().BeEquivalentTo(new[] { "Slug", "DisplayName" },
            "the handler-layer workspace option that feeds WorkspaceOptionResponse must carry no tenant/user IDs");
    }

    [Fact]
    public void WorkspaceCandidateSnapshot_TenantAndUserIdsAreInternalChallengeStateOnly()
    {
        // WorkspaceCandidateSnapshot is never returned to the browser: it is JSON-serialized only
        // into login_workspace_selection_challenges.candidate_workspaces_json, a durable,
        // server-side, hashed-challenge-keyed persisted row (see
        // EfLoginWorkspaceSelectionChallengeRepository). TenantId/UserId are required there so
        // SelectWorkspaceCommandHandler can resume the correct session once the caller picks a
        // workspace. This test documents and pins that intentional exception - it is not a gap to
        // "fix" by removing the IDs, and this type must never be reused as an API response shape.
        var properties = typeof(ONEVO.Application.Features.Auth.Login.RepositoryInterfaces.WorkspaceCandidateSnapshot)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        properties.Should().BeEquivalentTo(new[] { "TenantId", "UserId", "Slug", "DisplayName" },
            "WorkspaceCandidateSnapshot is internal persisted challenge state, never an API response");
    }

    [Fact]
    public void ProductionSource_ContainsNoProcessLocalWorkspaceSelectionChallengeStore()
    {
        var offenders = ScanProductionSourceFor("MemoryLoginWorkspaceSelectionChallenge")
            .Concat(ScanProductionSourceFor("InMemoryLoginWorkspaceSelectionChallenge"))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty(
            "PostgreSQL is the sole Phase 1 store for login_workspace_selection_challenges; no process-local fallback is approved");
    }

    [Fact]
    public void NewBaseLoginFilesContainNoDateTimeOffsetUtcNow()
    {
        var filesToCheck = new[]
        {
            FindSourceFile("ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseLogin", "BaseLoginCommandHandler.cs"),
            FindSourceFile("ONEVO.Application", "Features", "Auth", "Login", "Commands", "SelectWorkspace", "SelectWorkspaceCommandHandler.cs"),
            FindSourceFile("ONEVO.Infrastructure", "Persistence", "Repositories", "Auth", "Login", "EfBaseLoginCandidateRepository.cs"),
            FindSourceFile("ONEVO.Infrastructure", "Persistence", "Repositories", "Auth", "Login", "EfLoginWorkspaceSelectionChallengeRepository.cs"),
            FindSourceFile("ONEVO.Infrastructure", "Identity", "Passwords", "BaseLoginFixedWorkVerifier.cs"),
            FindSourceFile("ONEVO.Infrastructure", "Identity", "Tenancy", "TenantContextSwitcher.cs"),
            FindSourceFile("ONEVO.Infrastructure", "Services", "Auth", "Login", "LoginWorkspaceSelectionChallengeCleanupService.cs")
        };

        foreach (var path in filesToCheck)
        {
            File.ReadAllText(path).Should().NotContain("DateTimeOffset.UtcNow",
                $"{Path.GetFileName(path)} must use IDateTimeProvider, not DateTimeOffset.UtcNow directly");
        }
    }

    [Fact]
    public void NewBaseLoginRepositoriesHaveNoExpressionBodiedMethods()
    {
        var expressionBodiedMethodPattern = new System.Text.RegularExpressions.Regex(
            @"public\s+(async\s+)?[\w<>,\s\[\]?]+\s+\w+\([^)]*\)\s*=>",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        var filesToCheck = new[]
        {
            FindSourceFile("ONEVO.Infrastructure", "Persistence", "Repositories", "Auth", "Login", "EfBaseLoginCandidateRepository.cs"),
            FindSourceFile("ONEVO.Infrastructure", "Persistence", "Repositories", "Auth", "Login", "EfLoginWorkspaceSelectionChallengeRepository.cs")
        };

        foreach (var path in filesToCheck)
        {
            var text = File.ReadAllText(path);
            expressionBodiedMethodPattern.IsMatch(text).Should().BeFalse(
                $"{Path.GetFileName(path)} must use block-bodied methods only, per repository-persistence-boundary.md conventions");
        }
    }

    [Fact]
    public void AppsettingsAndEnvExample_GainNoTenantDiscoverySelectorKey()
    {
        var forbiddenTokens = new[] { "find_workspace", "findworkspace", "tenant_discovery", "workspace_selector" };
        var configFiles = new[]
        {
            Path.Combine(FindRepositoryRoot(), "src", "ONEVO.Api", "appsettings.json"),
            Path.Combine(FindRepositoryRoot(), "src", "ONEVO.Api", "appsettings.Development.json"),
            Path.Combine(FindRepositoryRoot(), ".env.example")
        };

        foreach (var configFile in configFiles)
        {
            var text = File.ReadAllText(configFile).ToLowerInvariant();
            foreach (var forbidden in forbiddenTokens)
            {
                text.Should().NotContain(forbidden, $"{Path.GetFileName(configFile)} must not add a tenant-discovery selector key");
            }
        }
    }

    [Fact]
    public void BaseLoginAndSelectWorkspaceHandlers_NeverReferenceTenantAuthPolicy()
    {
        // Product rule: eligibility is tenant status + user active only, never a per-tenant login
        // method flag. Neither handler may depend on ITenantAuthPolicyRepository/TenantAuthPolicy.
        // Note: a bare "MfaRequired" substring check is deliberately not used here - both handlers
        // legitimately call the pre-existing LoginMapper.ToMfaRequired(...) for per-user MFA, and
        // that method name itself contains the substring "MfaRequired", which would false-positive
        // against a naive text search. TenantAuthPolicy is the actual mechanism a tenant-wide MFA
        // flag would be gated through, so that check alone is the substantive guard.
        var handlerPaths = new[]
        {
            FindSourceFile("ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseLogin", "BaseLoginCommandHandler.cs"),
            FindSourceFile("ONEVO.Application", "Features", "Auth", "Login", "Commands", "SelectWorkspace", "SelectWorkspaceCommandHandler.cs")
        };

        foreach (var path in handlerPaths)
        {
            var text = File.ReadAllText(path);
            text.Should().NotContain("TenantAuthPolicy",
                $"{Path.GetFileName(path)} must not gate base-domain login eligibility on tenant_auth_policies");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate the repository root walking up from {AppContext.BaseDirectory}");
    }

    private static IReadOnlyList<string> ScanProductionSourceFor(string token)
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(objSegment, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains(token, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string FindSourceFile(string project, params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidateRoot = Path.Combine(directory.FullName, "src", project);
            if (Directory.Exists(candidateRoot))
            {
                var fullPath = Path.Combine(new[] { candidateRoot }.Concat(relativeSegments).ToArray());
                File.Exists(fullPath).Should().BeTrue($"expected {fullPath} to exist");
                return fullPath;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate src/{project} walking up from {AppContext.BaseDirectory}");
    }
}
