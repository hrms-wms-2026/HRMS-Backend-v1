using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class DevCertificateBootstrapperArchitectureTests
{
    [Fact]
    public void MkcertInstall_RestrictsTrustStoresToTheOsStoreAndSkipsJava()
    {
        var bootstrapper = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Api", "Configuration", "DevCertificateBootstrapper.cs"));
        var setupScript = File.ReadAllText(FindRepositoryPath("ops", "certs", "setup-local-certs.ps1"));

        Assert.Contains("[\"TRUST_STORES\"] = \"system\"", bootstrapper, StringComparison.Ordinal);
        Assert.Contains("$env:TRUST_STORES = 'system'", setupScript, StringComparison.Ordinal);
    }

    [Fact]
    public void MkcertInstall_ContinuesWhenTheSystemTrustStoreAlreadyHasTheCa()
    {
        var bootstrapper = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Api", "Configuration", "DevCertificateBootstrapper.cs"));
        var setupScript = File.ReadAllText(FindRepositoryPath("ops", "certs", "setup-local-certs.ps1"));

        Assert.Contains("CanContinueAfterMkcertInstall(", bootstrapper, StringComparison.Ordinal);
        Assert.Contains("installed in the system trust store", bootstrapper, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installed in the system trust store", setupScript, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var repositoryMarker = Path.Combine(directory.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(repositoryMarker))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
