using FluentAssertions;

namespace ONEVO.Tests.Architecture;

public sealed class TenantHostPasswordLoginRetirementArchitectureTests
{
    [Fact]
    public void LoginCommandFilesDoNotExist()
    {
        var srcRoot = Path.Combine(FindRepositoryRoot(), "src");
        var deletedDir = Path.Combine(
            srcRoot, "ONEVO.Application", "Features", "Auth", "Login", "Commands", "Login");

        Directory.Exists(deletedDir).Should().BeFalse(
            "the tenant-host password-login command folder (LoginCommand/LoginCommandHandler/LoginCommandValidator) was retired and must not be recreated");
    }

    [Fact]
    public void NoProductionSourceReferencesLoginCommand()
    {
        // \bLoginCommand\b matches the standalone retired type (e.g. "new LoginCommand(") but not
        // as a substring of a legitimate, unrelated identifier like BaseLoginCommand,
        // AdminLoginCommand, AdminGoogleLoginCommand, or BaseGoogleLoginCommand - none of those have
        // a word boundary immediately before "LoginCommand".
        var pattern = new System.Text.RegularExpressions.Regex(@"\bLoginCommand\b");
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        var offenders = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(objSegment, StringComparison.OrdinalIgnoreCase))
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .ToList();

        offenders.Should().BeEmpty(
            "no production code under src may reference the retired tenant-host LoginCommand type");
    }

    [Fact]
    public void AuthLoginControllerDoesNotReferenceLoginCommand()
    {
        var controllerPath = FindSourceFile(
            "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthLoginController.cs");
        var text = File.ReadAllText(controllerPath);

        text.Should().NotContain("new LoginCommand(");
        text.Should().Contain("Tenant-host password login is not supported.");
    }

    [Fact]
    public void NoProductionSourceClaimsTenantHostPasswordLoginIsSupported()
    {
        var forbiddenPhrases = new[]
        {
            "main login page",
            "tenant-host login is supported",
            "tenant-host password login is supported",
            "direct tenant login",
            "Tenant host: direct tenant login"
        };

        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var allFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(objSegment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var phrase in forbiddenPhrases)
        {
            var offenders = allFiles
                .Where(f => File.ReadAllText(f).Contains(phrase, StringComparison.OrdinalIgnoreCase))
                .ToList();

            offenders.Should().BeEmpty(
                $"no production source may claim or imply tenant-host password login is supported (found phrase: \"{phrase}\")");
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
