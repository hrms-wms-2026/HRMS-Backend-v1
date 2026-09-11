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
        RunMkcert(mkcertExecutable, ["-install"]);

        var sanList = new List<string> { rootDomain, "127.0.0.1", "::1" };
        sanList.AddRange(TenantSubdomains.Select(sub => $"{sub}.{rootDomain}"));
        sanList.Add($"*.{rootDomain}");

        var generateArgs = new List<string> { "-key-file", resolvedKeyPath, "-cert-file", resolvedCertPath };
        generateArgs.AddRange(sanList);
        RunMkcert(mkcertExecutable, generateArgs);

        Console.WriteLine($"[CERTIFICATE] Generated {resolvedCertPath}");
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

    private static void RunMkcert(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executable}'.");
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"mkcert exited with code {process.ExitCode} running: {executable} {string.Join(' ', arguments)}");
        }
    }
}
