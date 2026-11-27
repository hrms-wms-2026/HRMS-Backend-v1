using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Improvements task 26: no request path blocks a thread-pool thread on async work. Under load,
/// sync-over-async (.GetAwaiter().GetResult(), .Wait(), task.Result) starves the pool and gives
/// sudden latency spikes; .ContinueWith(t => t.Result) also wraps failures in AggregateException.
/// </summary>
public sealed class NoBlockingAsyncArchitectureTests
{
    // Startup tooling, never on a request path.
    private static readonly string[] Allowed =
    [
        Path.Combine("ONEVO.Api", "Configuration", "DevCertificateBootstrapper.cs"),
    ];

    private static readonly Regex Blocking = new(
        @"\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(\)|\.ContinueWith\(",
        RegexOptions.Compiled);

    [Fact]
    public void Source_has_no_sync_over_async_calls()
    {
        var src = FindRepositoryPath("src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !Allowed.Any(allowed => path.EndsWith(allowed, StringComparison.Ordinal)))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, line, number: index + 1))
                .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && Blocking.IsMatch(x.line)))
            .Select(x => $"{Path.GetRelativePath(src, x.path)}:{x.number}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Use await instead of blocking:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
