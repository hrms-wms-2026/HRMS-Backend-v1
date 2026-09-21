using System.Diagnostics;

namespace ONEVO.Api.Configuration;

/// <summary>
/// Development only: generates the local mkcert certificate Kestrel:Certificates:Default
/// points at if it is missing, so a freshly cloned repo can run `dotnet run` without first
/// running ops/certs/setup-local-certs.ps1 by hand. Mirrors that script's mkcert invocation
/// (same SAN list: root domain, tenant subdomains, and the wildcard) so both paths produce an
/// equivalent certificate. Never runs outside Development - Production/Staging terminate TLS
/// with a real certificate authority provisioned outside this repo.
/// </summary>
public static class DevCertificateBootstrapper
{
    private static readonly string[] TenantSubdomains = ["acme", "dapi", "admin"];

    public static void EnsureCertificateExists(IConfiguration configuration, string contentRootPath)
    {
        var certPath = configuration["Kestrel:Certificates:Default:Path"];
        var keyPath = configuration["Kestrel:Certificates:Default:KeyPath"];
        if (string.IsNullOrWhiteSpace(certPath) || string.IsNullOrWhiteSpace(keyPath))
        {
            return; // No Kestrel certificate configured for this environment - nothing to do.
        }

        var resolvedCertPath = Path.GetFullPath(Path.Combine(contentRootPath, certPath));
        var resolvedKeyPath = Path.GetFullPath(Path.Combine(contentRootPath, keyPath));
        if (File.Exists(resolvedCertPath) && File.Exists(resolvedKeyPath))
        {
            return;
        }

        var rootDomain = configuration["Tenancy:RootDomain"];
        if (string.IsNullOrWhiteSpace(rootDomain))
        {
            throw new InvalidOperationException(
                $"Local HTTPS certificate not found at '{resolvedCertPath}' and Tenancy:RootDomain is " +
                "not configured, so it cannot be generated automatically. Run " +
                "ops/certs/setup-local-certs.ps1 from the repository root.");
        }

        var mkcertExecutable = FindMkcertExecutable();
        if (mkcertExecutable is null)
        {
            throw new InvalidOperationException(
                $"Local HTTPS certificate not found at '{resolvedCertPath}' and mkcert was not found on " +
                "PATH or at C:\\tools\\mkcert\\mkcert.exe to generate one automatically. Install mkcert " +
                "(https://github.com/FiloSottile/mkcert) and run ops/certs/setup-local-certs.ps1 from the " +
                "repository root, or run mkcert manually.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedCertPath)!);

        Console.WriteLine("[CERTIFICATE] Local HTTPS certificate not found - generating with mkcert...");
        RunMkcert(mkcertExecutable, ["-install"], allowSecondaryTrustStoreFailure: true);

        var sanList = new List<string> { rootDomain, "127.0.0.1", "::1" };
        sanList.AddRange(TenantSubdomains.Select(sub => $"{sub}.{rootDomain}"));
        sanList.Add($"*.{rootDomain}");

        var generateArgs = new List<string> { "-key-file", resolvedKeyPath, "-cert-file", resolvedCertPath };
        generateArgs.AddRange(sanList);
        RunMkcert(mkcertExecutable, generateArgs, allowSecondaryTrustStoreFailure: false);

        Console.WriteLine($"[CERTIFICATE] Generated {resolvedCertPath}");
    }

    /// <summary>
    /// mkcert -install can return a non-zero exit after the OS trust store already has the
    /// local CA, because it also tries secondary stores (a JDK keystore under Program Files
    /// needs elevation). Kestrel and Chromium browsers only need the OS store, so generation
    /// can continue in that case.
    /// </summary>
    public static bool CanContinueAfterMkcertInstall(int exitCode, string output)
    {
        if (exitCode == 0)
        {
            return true;
        }

        return output.Contains("installed in the system trust store", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindMkcertExecutable()
    {
        var pathMatch = FindOnPath(OperatingSystem.IsWindows() ? "mkcert.exe" : "mkcert");
        if (pathMatch is not null)
        {
            return pathMatch;
        }

        const string wellKnownWindowsPath = @"C:\tools\mkcert\mkcert.exe";
        return OperatingSystem.IsWindows() && File.Exists(wellKnownWindowsPath)
            ? wellKnownWindowsPath
            : null;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void RunMkcert(
        string executable,
        IEnumerable<string> arguments,
        bool allowSecondaryTrustStoreFailure)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Restrict to the OS trust store. mkcert otherwise also tries a JDK keystore, which
        // fails without elevation on a typical Windows JDK install under Program Files.
        startInfo.Environment["TRUST_STORES"] = "system";
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executable}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.Write(output);
            if (!output.EndsWith('\n') && !output.EndsWith('\r'))
            {
                Console.WriteLine();
            }
        }

        if (process.ExitCode == 0)
        {
            return;
        }

        if (allowSecondaryTrustStoreFailure && CanContinueAfterMkcertInstall(process.ExitCode, output))
        {
            Console.WriteLine(
                "[CERTIFICATE] mkcert -install reported a secondary trust-store error. " +
                "The Windows/system store already has the local CA, so certificate generation will continue.");
            return;
        }

        throw new InvalidOperationException(
            $"mkcert exited with code {process.ExitCode} running: {executable} {string.Join(' ', arguments)}" +
            (string.IsNullOrWhiteSpace(output) ? string.Empty : $"{Environment.NewLine}{output}"));
    }
}
