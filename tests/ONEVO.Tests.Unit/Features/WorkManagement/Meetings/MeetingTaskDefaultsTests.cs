using Moq;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

public class MeetingTaskDefaultsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static (MeetingTaskDefaults Defaults, Mock<ITaskCategoryRepository> Categories) Build(params TaskCategory[] existing)
    {
        var categories = new Mock<ITaskCategoryRepository>();
        categories.Setup(x => x.GetByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", Category = TaskStatusCategories.Active },
        ]);
        return (new MeetingTaskDefaults(categories.Object, statuses.Object), categories);
    }

    private static TaskCategory Category(string name, int order, string kind, string? systemKey = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = name, DisplayOrder = order,
        Kind = kind, SystemKey = systemKey, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task ResolveAsync_ProjectWithoutAMeetingCategory_AddsTheSystemMeetingCategoryWithTheMeetingKind()
    {
        var (defaults, categories) = Build(Category("task", 0, TaskCategoryKinds.Task), Category("bug", 1, TaskCategoryKinds.Bug));

        var result = await defaults.ResolveAsync(TenantId, ProjectId, UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        categories.Verify(x => x.AddAsync(It.Is<TaskCategory>(c =>
            c.Id == result.Value.CategoryId && c.Name == "meeting" && c.SystemKey == TaskCategorySystemKeys.Meeting
            && c.Kind == TaskCategoryKinds.Meeting && c.DisplayOrder == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_ExistingMeetingCategory_IsReused()
    {
        var meeting = Category("meeting", 4, TaskCategoryKinds.Meeting, TaskCategorySystemKeys.Meeting);
        var (defaults, categories) = Build(Category("task", 0, TaskCategoryKinds.Task), meeting);

        var result = await defaults.ResolveAsync(TenantId, ProjectId, UserId, CancellationToken.None);

        Assert.Equal(meeting.Id, result.Value.CategoryId);
        categories.Verify(x => x.AddAsync(It.IsAny<TaskCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The meeting category is found by its system key, never by its kind or its name.
    [Fact]
    public async Task ResolveAsync_AUserCategoryNamedMeeting_IsNotTreatedAsTheMeetingCategory()
    {
        var lookalike = Category("Meeting", 0, TaskCategoryKinds.Task);
        var (defaults, categories) = Build(lookalike);

        var result = await defaults.ResolveAsync(TenantId, ProjectId, UserId, CancellationToken.None);

        Assert.NotEqual(lookalike.Id, result.Value.CategoryId);
        categories.Verify(x => x.AddAsync(It.Is<TaskCategory>(c => c.SystemKey == TaskCategorySystemKeys.Meeting), It.IsAny<CancellationToken>()), Times.Once);
    }
}
