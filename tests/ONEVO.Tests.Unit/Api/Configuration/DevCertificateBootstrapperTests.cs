using FluentAssertions;
using ONEVO.Api.Configuration;

namespace ONEVO.Tests.Unit.Api.Configuration;

public sealed class DevCertificateBootstrapperTests
{
    [Fact]
    public void CanContinueAfterMkcertInstall_WhenExitCodeIsZero()
    {
        DevCertificateBootstrapper
            .CanContinueAfterMkcertInstall(0, "The local CA is now installed in the system trust store!")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CanContinueAfterMkcertInstall_WhenSystemStoreAlreadyHasCaButJavaCacertsIsDenied()
    {
        const string output =
            """
            The local CA is already installed in the system trust store!
            ERROR: failed to execute "keytool -importcert": exit status 1

            Warning: use -cacerts option to access cacerts keystore
            Certificate was added to keystore
            keytool error: java.io.FileNotFoundException: C:\Program Files\Eclipse Adoptium\jdk-17.0.20.101-hotspot\lib\security\cacerts (Access is denied)
            """;

        DevCertificateBootstrapper.CanContinueAfterMkcertInstall(1, output).Should().BeTrue();
    }

    [Fact]
    public void CanContinueAfterMkcertInstall_WhenSystemStoreWasJustInstalledButJavaCacertsFails()
    {
        const string output =
            """
            The local CA is now installed in the system trust store!
            ERROR: failed to execute "keytool -importcert": exit status 1
            """;

        DevCertificateBootstrapper.CanContinueAfterMkcertInstall(1, output).Should().BeTrue();
    }

    [Fact]
    public void CanContinueAfterMkcertInstall_WhenInstallFailedWithoutSystemStoreSuccess()
    {
        const string output =
            """
            ERROR: failed to execute "certutil -addstore -f": Access is denied.
            """;

        DevCertificateBootstrapper.CanContinueAfterMkcertInstall(1, output).Should().BeFalse();
    }

    [Fact]
    public void CanContinueAfterMkcertInstall_WhenInstallFailedWithEmptyOutput()
    {
        DevCertificateBootstrapper.CanContinueAfterMkcertInstall(1, string.Empty).Should().BeFalse();
    }
}
