using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class BulkOnboardingEntityTests
{
    [Fact]
    public void BulkOnboardingBatch_DefaultsToMappingPendingStatus()
    {
        var batch = new BulkOnboardingBatch();
        Assert.Equal(BulkOnboardingBatchStatus.MappingPending, batch.Status);
    }

    [Fact]
    public void BulkOnboardingBatchRow_DefaultsToPendingMappingStatus()
    {
        var row = new BulkOnboardingBatchRow();
        Assert.Equal(BulkOnboardingBatchRowStatus.PendingMapping, row.Status);
    }
}
