using FluentAssertions;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Enums;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public class TrayDeviceAuthorizationTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = CreatedAt.AddMinutes(10);

    [Fact]
    public void IsExpired_WhenNowEqualsExpiresAt_ReturnsTrue()
    {
        var authorization = Authorization(DeviceAuthorizationStatus.Pending);

        authorization.IsExpired(ExpiresAt).Should().BeTrue();
    }

    [Fact]
    public void CanApprove_OnlyPendingUnexpiredAuthorization_ReturnsTrue()
    {
        var authorization = Authorization(DeviceAuthorizationStatus.Pending);

        authorization.CanApprove(ExpiresAt.AddSeconds(-1)).Should().BeTrue();
        authorization.CanApprove(ExpiresAt).Should().BeFalse();
        authorization.CanApprove(ExpiresAt.AddSeconds(1)).Should().BeFalse();
        Authorization(DeviceAuthorizationStatus.Approved).CanApprove(CreatedAt).Should().BeFalse();
    }

    [Fact]
    public void CanConsume_OnlyApprovedUnexpiredAuthorization_ReturnsTrue()
    {
        var authorization = Authorization(DeviceAuthorizationStatus.Approved);

        authorization.CanConsume(ExpiresAt.AddSeconds(-1)).Should().BeTrue();
        authorization.CanConsume(ExpiresAt).Should().BeFalse();
        authorization.CanConsume(ExpiresAt.AddSeconds(1)).Should().BeFalse();
        Authorization(DeviceAuthorizationStatus.Pending).CanConsume(CreatedAt).Should().BeFalse();
    }

    private static TrayDeviceAuthorization Authorization(DeviceAuthorizationStatus status) => new()
    {
        Id = Guid.NewGuid(),
        DeviceCodeHash = "device-code-hash",
        UserCodeHash = "user-code-hash",
        DeviceFingerprintHash = "fingerprint-hash",
        DeviceName = "DESKTOP-7K2Q",
        DeviceOs = "Windows 11",
        ClientVersion = "1.0.0",
        Status = status,
        CreatedAt = CreatedAt,
        ExpiresAt = ExpiresAt,
    };
}
