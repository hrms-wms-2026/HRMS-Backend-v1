using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DefaultTaskCategoryTemplateTests
{
    [Fact]
    public void BuildRows_IncludesExactlyOneSystemMeetingCategoryLast()
    {
        var rows = DefaultTaskCategoryTemplate.BuildRows(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(new[] { "task", "bug", "story", "feature", "meeting" }, rows.OrderBy(r => r.DisplayOrder).Select(r => r.Name));
        var meeting = Assert.Single(rows, r => r.SystemKey is not null);
        Assert.Equal(TaskCategorySystemKeys.Meeting, meeting.SystemKey);
        Assert.Equal(4, meeting.DisplayOrder);
    }
}
