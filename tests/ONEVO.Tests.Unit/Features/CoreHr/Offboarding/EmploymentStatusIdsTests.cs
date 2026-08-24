using FluentAssertions;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmploymentStatusIdsTests
{
    [Fact]
    public void OffboardingAndResigned_AreDistinctFromExistingStatuses()
    {
        var ids = new[]
        {
            EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave, EmploymentStatusIds.Suspended,
            EmploymentStatusIds.Terminated, EmploymentStatusIds.Offboarding, EmploymentStatusIds.Resigned,
        };
        ids.Should().OnlyHaveUniqueItems();
        EmploymentStatusIds.Offboarding.Should().Be(5);
        EmploymentStatusIds.Resigned.Should().Be(6);
    }
}
