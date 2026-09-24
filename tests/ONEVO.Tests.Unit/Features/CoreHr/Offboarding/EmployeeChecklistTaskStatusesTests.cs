using FluentAssertions;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmployeeChecklistTaskStatusesTests
{
    [Fact]
    public void AllFourStatuses_AreDistinct()
    {
        new[]
        {
            EmployeeChecklistTaskStatuses.Pending, EmployeeChecklistTaskStatuses.InProgress,
            EmployeeChecklistTaskStatuses.Completed, EmployeeChecklistTaskStatuses.Bypassed,
        }.Should().OnlyHaveUniqueItems();
    }
}
