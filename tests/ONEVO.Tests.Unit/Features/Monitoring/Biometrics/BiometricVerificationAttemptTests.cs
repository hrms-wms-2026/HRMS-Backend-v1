using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class BiometricVerificationAttemptTests
{
    private static BiometricVerificationAttempt NewAttempt() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        DeviceRegistrationId = Guid.NewGuid(),
        Purpose = BiometricAttemptPurpose.Enrollment,
        Status = BiometricAttemptStatus.Created,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Theory]
    [InlineData(BiometricAttemptStatus.Created, BiometricAttemptStatus.Capturing, true)]
    [InlineData(BiometricAttemptStatus.Capturing, BiometricAttemptStatus.Verifying, true)]
    [InlineData(BiometricAttemptStatus.Verifying, BiometricAttemptStatus.Verified, true)]
    [InlineData(BiometricAttemptStatus.Verifying, BiometricAttemptStatus.Rejected, true)]
    [InlineData(BiometricAttemptStatus.Verifying, BiometricAttemptStatus.ProviderError, true)]
    [InlineData(BiometricAttemptStatus.Created, BiometricAttemptStatus.Expired, true)]
    [InlineData(BiometricAttemptStatus.Capturing, BiometricAttemptStatus.Expired, true)]
    [InlineData(BiometricAttemptStatus.Verified, BiometricAttemptStatus.Capturing, false)]
    [InlineData(BiometricAttemptStatus.Rejected, BiometricAttemptStatus.Verified, false)]
    [InlineData(BiometricAttemptStatus.Created, BiometricAttemptStatus.Verified, false)]
    [InlineData(BiometricAttemptStatus.Expired, BiometricAttemptStatus.Capturing, false)]
    public void TryTransition_EnforcesAllowedStateGraph(string from, string to, bool expectedAllowed)
    {
        var attempt = NewAttempt();
        attempt.Status = from;

        var allowed = attempt.TryTransition(to, out var previous);

        Assert.Equal(expectedAllowed, allowed);
        Assert.Equal(from, previous);
        Assert.Equal(expectedAllowed ? to : from, attempt.Status);
    }
}
