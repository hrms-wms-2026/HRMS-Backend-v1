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
