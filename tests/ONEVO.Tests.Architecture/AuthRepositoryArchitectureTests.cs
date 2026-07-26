using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards readability of security-sensitive auth repositories. Scoped narrowly to
/// EfInvitationTokenRepository only; other repositories still have known expression-bodied
/// members and are cleaned up in later phases.
/// </summary>
public sealed class AuthRepositoryArchitectureTests
{
    [Fact]
    public void EfInvitationTokenRepository_HasNoExpressionBodiedMembers()
    {
        var path = FindRepositoryFile();
        var source = File.ReadAllText(path);

        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = source
            .Split('\n')
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => expressionBodiedMember.IsMatch(entry.Line))
            .Select(entry => $"line {entry.Number}: {entry.Line.Trim()}")
            .ToList();

        Assert.True(
            offendingLines.Count == 0,
            "EfInvitationTokenRepository must use block-bodied members, but found: "
                + string.Join("; ", offendingLines));
    }

    private static string FindRepositoryFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Persistence",
                "Repositories",
                "Auth",
                "Invite",
                "EfInvitationTokenRepository.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate EfInvitationTokenRepository.cs walking up from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Phase 1F-A guard: scoped to only the auth/core methods of EfAuthRepository
    /// (constructor, user, refresh token, session, password reset token, MFA). Role,
    /// permission, user-role, feature-access, audit-log, and role-template methods still
    /// have expression-bodied members and are covered by later phases.
    /// </summary>
    [Fact]
    public void EfAuthRepository_AuthCoreMethods_HaveNoExpressionBodiedMembers()
    {
        var path = FindAuthRepositoryFile();
        var source = File.ReadAllText(path);
        var lines = source.Split('\n');

        // Full parameter signatures (not bare method names) so this guard cannot collide with
        // later-phase overloads that share a method name (e.g. Role.AddAsync, RoleTemplate.AddAsync,
        // AuditLog.AddAsync, UserRole.AddAsync, UserRole.ListActiveByUserIdAsync, Role.Remove,
        // RoleTemplate's explicit IRoleTemplateRepository.GetByIdAsync) and are out of scope here.
        var phase1FaMethodSignatures = new[]
        {
            "EfAuthRepository(ApplicationDbContext db)",
            "GetByNormalizedEmailAsync(string normalizedEmail",
            "GetActiveByNormalizedEmailAsync(string normalizedEmail",
            "GetByIdAsync(Guid userId",
            "GetByTenantAndEmailAsync(Guid tenantId, string normalizedEmail",
            "AddAsync(User user",
            "GetByHashAsync(string tokenHash",
            "ListActiveByUserIdAsync(Guid userId, CancellationToken",
            "AddAsync(RefreshToken refreshToken",
            "GetLatestActiveByUserIdAsync(Guid userId",
            "ISessionRepository.GetByIdAsync(Guid sessionId",
            "ISessionRepository.GetByKeyHashAsync(string keyHash",
            "ISessionRepository.RevokeByKeyHashAsync(string keyHash",
            "ISessionRepository.RevokeByIdAsync(Guid sessionId",
            "AddAsync(Session session",
            "GetResetTokenByHashAsync(string tokenHash",
            "ListValidByUserIdAsync(",
            "AddAsync(PasswordResetToken resetToken",
            "GetTotpAsync(Guid userId",
            "AddAsync(UserMfa mfa",
            "Remove(UserMfa mfa)",
        };

        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var explicitInterfaceExpressionBodiedMember = new Regex(
            @"^\s*(async\s+)?Task[<>\w\?]*\s+I\w+\.\w+\([^)]*\)\s*=>",
            RegexOptions.Compiled);

        var voidRemoveExpressionBodiedMember = new Regex(
            @"void\s+Remove\(UserMfa\s+\w+\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isPhase1FaMethodLine = phase1FaMethodSignatures.Any(signature => line.Contains(signature));
            if (!isPhase1FaMethodLine)
            {
                continue;
            }

            var isOffending =
                expressionBodiedMember.IsMatch(line)
                || explicitInterfaceExpressionBodiedMember.IsMatch(line)
                || voidRemoveExpressionBodiedMember.IsMatch(line);

            if (isOffending)
            {
                offendingLines.Add($"line {i + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            offendingLines.Count == 0,
            "EfAuthRepository Phase 1F-A auth/core methods must use block-bodied members, but found: "
                + string.Join("; ", offendingLines));
    }

    private static string FindAuthRepositoryFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Persistence",
                "Repositories",
                "Auth",
                "Login",
                "EfAuthRepository.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate EfAuthRepository.cs walking up from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Phase 1F-B guard: scoped to only the Role and RolePermission methods of
    /// EfAuthRepository (IRoleRepository and IRolePermissionRepository). Permission,
    /// user-role, feature-access, audit-log, and role-template methods still have
    /// expression-bodied members and are covered by later phases.
    /// </summary>
    [Fact]
    public void EfAuthRepository_RoleCoreMethods_HaveNoExpressionBodiedMembers()
    {
        var path = FindAuthRepositoryFile();
        var source = File.ReadAllText(path);
        var lines = source.Split('\n');

        // Full parameter signatures (not bare method names) so this guard cannot collide with
        // out-of-scope overloads that share a method name (e.g. AddAsync is also used by User,
        // RefreshToken, Session, PasswordResetToken, UserMfa, UserRole, AuditLog, RoleTemplate;
        // Remove is also used by UserMfa) and are out of scope here.
        var phase1FbMethodSignatures = new[]
        {
            "GetRoleByIdAsync(Guid roleId",
            "GetByIdForTenantAsync(Guid tenantId, Guid roleId",
            "GetByNameForTenantAsync(Guid tenantId, string name",
            "GetBySourceTemplateForTenantAsync(",
            "ListByTenantAsync(Guid tenantId",
            "AddAsync(Role role",
            "Remove(Role role)",
            "ListByRoleAsync(Guid roleId",
            "AddRangeAsync(IEnumerable<RolePermission> rolePermissions",
            "RemoveRange(IEnumerable<RolePermission> rolePermissions)",
        };

        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var voidRemoveExpressionBodiedMember = new Regex(
            @"void\s+Remove\(Role\s+\w+\)\s*=>",
            RegexOptions.Compiled);

        var voidRemoveRangeExpressionBodiedMember = new Regex(
            @"void\s+RemoveRange\(IEnumerable<RolePermission>\s+\w+\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isPhase1FbMethodLine = phase1FbMethodSignatures.Any(signature => line.Contains(signature));
            if (!isPhase1FbMethodLine)
            {
                continue;
            }

            var isOffending =
                expressionBodiedMember.IsMatch(line)
                || voidRemoveExpressionBodiedMember.IsMatch(line)
                || voidRemoveRangeExpressionBodiedMember.IsMatch(line);

            if (isOffending)
            {
                offendingLines.Add($"line {i + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            offendingLines.Count == 0,
            "EfAuthRepository Phase 1F-B role/role-permission methods must use block-bodied "
                + "members, but found: " + string.Join("; ", offendingLines));
    }

    /// <summary>
    /// Phase 1F-C guard: scoped to only the IPermissionRepository methods of EfAuthRepository.
    /// User permission override, user-role, feature-access, audit-log, and role-template
    /// methods still have expression-bodied members and are covered by later phases.
    /// </summary>
    [Fact]
    public void EfAuthRepository_PermissionCoreMethods_HaveNoExpressionBodiedMembers()
    {
        var path = FindAuthRepositoryFile();
        var source = File.ReadAllText(path);
        var lines = source.Split('\n');

        // Full parameter signatures (not bare method names) so this guard cannot collide with
        // out-of-scope overloads or methods elsewhere in the file (e.g. UserRole's
        // ListActiveByUserIdAsync, AddAsync) that are out of scope here.
        var phase1FcMethodSignatures = new[]
        {
            "GetByCodeAsync(string code",
            "GetByIdsAsync(IEnumerable<Guid> ids",
            "GetByCodesAsync(IEnumerable<string> codes",
            "UserHasPermissionCodeAsync(",
            "ListRolePermissionCodesAsync(",
            "ListRolePermissionCodesWithModulesAsync(",
        };

        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isPhase1FcMethodLine = phase1FcMethodSignatures.Any(signature => line.Contains(signature));
            if (!isPhase1FcMethodLine)
            {
                continue;
            }

            var isOffending = expressionBodiedMember.IsMatch(line);

            if (isOffending)
            {
                offendingLines.Add($"line {i + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            offendingLines.Count == 0,
            "EfAuthRepository Phase 1F-C permission methods must use block-bodied members, but found: "
                + string.Join("; ", offendingLines));
    }

    /// <summary>
    /// Phase 1F-D guard: scoped to only the IUserPermissionOverrideRepository and
    /// IUserRoleRepository methods of EfAuthRepository. Feature-access, audit-log, and
    /// role-template methods still have expression-bodied members and are covered by later
    /// phases.
    /// </summary>
    [Fact]
    public void EfAuthRepository_UserAccessCoreMethods_HaveNoExpressionBodiedMembers()
    {
        var path = FindAuthRepositoryFile();
        var source = File.ReadAllText(path);
        var lines = source.Split('\n');

        // Matched by full parameter signature (not bare method name) so this guard cannot
        // collide with out-of-scope overloads or methods elsewhere in the file (e.g.
        // RefreshToken's single-line ListActiveByUserIdAsync(Guid userId, CancellationToken),
        // RoleTemplate's AddAsync) that are out of scope here. UserRole's ListActiveByUserIdAsync
        // and ListUserIdsByRoleAsync signatures span multiple lines, so each is matched by
        // looking ahead for its "DateTimeOffset now" parameter.
        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isPhase1FdMethodLine =
                line.Contains("ListForUserAsync(")
                || (line.Contains("ListUserIdsByRoleAsync(")
                    && lines.Skip(i).Take(3).Any(l => l.Contains("DateTimeOffset now")))
                || line.Contains("AddAsync(UserRole userRole")
                || (line.Contains("ListActiveByUserIdAsync(")
                    && lines.Skip(i).Take(3).Any(l => l.Contains("DateTimeOffset now")));

            if (!isPhase1FdMethodLine)
            {
                continue;
            }

            var isOffending = expressionBodiedMember.IsMatch(line);

            if (isOffending)
            {
                offendingLines.Add($"line {i + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            offendingLines.Count == 0,
            "EfAuthRepository Phase 1F-D user permission override / user role methods must use "
                + "block-bodied members, but found: " + string.Join("; ", offendingLines));
    }

    /// <summary>
    /// Phase 1F-E guard: final full-file check. All prior phases (1F-A through 1F-D) plus
    /// this phase's IFeatureAccessGrantRepository, IAuditLogRepository, and
    /// IRoleTemplateRepository methods have been converted to block bodies, so this guard
    /// fails on any expression-bodied member anywhere in EfAuthRepository.cs, including
    /// public methods, explicit interface implementations, and void methods.
    /// </summary>
    [Fact]
    public void EfAuthRepository_HasNoExpressionBodiedMembers()
    {
        var path = FindAuthRepositoryFile();
        var source = File.ReadAllText(path);
        var lines = source.Split('\n');

        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var explicitInterfaceExpressionBodiedMember = new Regex(
            @"^\s*(async\s+)?(Task|void)[<>\w\?]*\s+I\w+\.\w+\([^)]*\)\s*=>",
            RegexOptions.Compiled);

        var voidExpressionBodiedMember = new Regex(
            @"^\s*void\s+\w+\([^)]*\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isOffending =
                expressionBodiedMember.IsMatch(line)
                || explicitInterfaceExpressionBodiedMember.IsMatch(line)
                || voidExpressionBodiedMember.IsMatch(line);

            if (isOffending)
            {
                offendingLines.Add($"line {i + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            offendingLines.Count == 0,
            "EfAuthRepository must have no expression-bodied members remaining, but found: "
                + string.Join("; ", offendingLines));
    }

    /// <summary>
    /// Phase 1G guard: full-file check for EfGlobalEmailDirectoryRepository. This repository is
    /// intentionally tiny (constructor + UpsertAsync), so unlike EfAuthRepository there are no
    /// sub-phases -- the whole file must be free of expression-bodied members.
    /// </summary>
    [Fact]
    public void EfGlobalEmailDirectoryRepository_HasNoExpressionBodiedMembers()
    {
        var path = FindGlobalEmailDirectoryRepositoryFile();
        var source = File.ReadAllText(path);
        var lines = source.Split('\n');

        var expressionBodiedMember = new Regex(
            @"(public|private|protected|internal)[^\n{;]*\)\s*=>",
            RegexOptions.Compiled);

        var offendingLines = lines
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => expressionBodiedMember.IsMatch(entry.Line))
            .Select(entry => $"line {entry.Number}: {entry.Line.Trim()}")
            .ToList();

        Assert.True(
            offendingLines.Count == 0,
            "EfGlobalEmailDirectoryRepository must have no expression-bodied members, but found: "
                + string.Join("; ", offendingLines));
    }

    /// <summary>
    /// Phase 1G guard: proves the readability cleanup did not disturb the intentional
    /// PostgreSQL upsert statement. This method deliberately uses raw SQL with
    /// ON CONFLICT DO NOTHING instead of EF load/check/insert logic to avoid a race between
    /// concurrent upserts for the same email + tenant, so the exact SQL markers must survive.
    /// </summary>
    [Fact]
    public void EfGlobalEmailDirectoryRepository_PreservesPostgresUpsertSql()
    {
        var path = FindGlobalEmailDirectoryRepositoryFile();
        var source = File.ReadAllText(path);

        Assert.Contains("ExecuteSqlInterpolatedAsync", source);
        Assert.Contains("INSERT INTO global_email_directory", source);
        Assert.Contains("ON CONFLICT (email, tenant_id) DO NOTHING", source);
    }

    private static string FindGlobalEmailDirectoryRepositoryFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Persistence",
                "Repositories",
                "Auth",
                "Login",
                "EfGlobalEmailDirectoryRepository.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate EfGlobalEmailDirectoryRepository.cs walking up from " + AppContext.BaseDirectory);
    }
}
