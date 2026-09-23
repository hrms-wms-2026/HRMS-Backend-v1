using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCommentEntityTests
{
    [Fact]
    public void TaskComment_Defaults_IsNotEditedAndHasNoParent()
    {
        var comment = new TaskComment();

        Assert.False(comment.IsEdited);
        Assert.Null(comment.ParentCommentId);
        Assert.False(comment.IsDeleted);
    }

    [Fact]
    public void TaskCommentLog_DefaultsAction_ToEdited()
    {
        var log = new TaskCommentLog();

        Assert.Equal(TaskCommentLogActions.Edited, log.Action);
    }
}
