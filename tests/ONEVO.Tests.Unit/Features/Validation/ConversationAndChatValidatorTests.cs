using FluentValidation.TestHelper;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;
using ONEVO.Application.Features.Conversations.Commands.EditConversationMessage;
using ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;
using ONEVO.Application.Features.Inbox.Chats;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;
using static ONEVO.Tests.Unit.Features.Validation.ValidationRuleExtensionsTests;

namespace ONEVO.Tests.Unit.Features.Validation;

/// <summary>Task 19: user-written text, rich text and people lists in chats, conversations and task comments.</summary>
public sealed class ConversationAndChatValidatorTests
{
    private const string Script = "<p>hi</p><script>alert(1)</script>";

    // ---- PostConversationMessageCommand ----

    [Fact]
    public void PostMessage_Valid_Passes()
        => new PostConversationMessageCommandValidator()
            .TestValidate(new PostConversationMessageCommand(Guid.NewGuid(), "<p>Hello <b>team</b></p>", Ids(2), Ids(5), "justification"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void PostMessage_EmptyBody_IsLeftToTheHandler_BecauseFilesMayCarryTheMessage()
        => new PostConversationMessageCommandValidator()
            .TestValidate(new PostConversationMessageCommand(Guid.NewGuid(), "", null, Ids(1)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void PostMessage_ActiveContent_Fails()
        => new PostConversationMessageCommandValidator()
            .TestValidate(new PostConversationMessageCommand(Guid.NewGuid(), Script, null))
            .ShouldHaveValidationErrorFor(x => x.BodyHtml).WithErrorMessage(RichTextRules.ActiveContentError);

    [Fact]
    public void PostMessage_BodyOverColumn_Fails()
        => new PostConversationMessageCommandValidator()
            .TestValidate(new PostConversationMessageCommand(Guid.NewGuid(), new string('a', 10_001), null))
            .ShouldHaveValidationErrorFor(x => x.BodyHtml);

    [Fact]
    public void PostMessage_TooManyAttachmentsMentionsOrLongTag_Fails()
    {
        var result = new PostConversationMessageCommandValidator()
            .TestValidate(new PostConversationMessageCommand(Guid.NewGuid(), "hi", Ids(251), Ids(6), new string('t', 21)));
        result.ShouldHaveValidationErrorFor(x => x.MentionEmployeeIds);
        result.ShouldHaveValidationErrorFor(x => x.AttachmentFileIds);
        result.ShouldHaveValidationErrorFor(x => x.Tag);
    }

    // ---- EditConversationMessageCommand ----

    [Fact]
    public void EditMessage_Valid_Passes()
        => new EditConversationMessageCommandValidator()
            .TestValidate(new EditConversationMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "<p>fixed</p>", Ids(1)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EditMessage_ActiveContentOrTooLong_Fails()
    {
        var validator = new EditConversationMessageCommandValidator();
        validator.TestValidate(new EditConversationMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "<iframe src=x>", null))
            .ShouldHaveValidationErrorFor(x => x.BodyHtml);
        validator.TestValidate(new EditConversationMessageCommand(Guid.NewGuid(), Guid.NewGuid(), new string('a', 10_001), null))
            .ShouldHaveValidationErrorFor(x => x.BodyHtml);
        validator.TestValidate(new EditConversationMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "ok", Ids(251)))
            .ShouldHaveValidationErrorFor(x => x.MentionEmployeeIds);
    }

    // ---- AddConversationParticipantsCommand ----

    [Fact]
    public void AddParticipants_UpTo250_Passes_More_Fails()
    {
        var validator = new AddConversationParticipantsCommandValidator();
        validator.TestValidate(new AddConversationParticipantsCommand(Guid.NewGuid(), Ids(250))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new AddConversationParticipantsCommand(Guid.NewGuid(), Ids(251)))
            .ShouldHaveValidationErrorFor(x => x.EmployeeIds);
    }

    // ---- Inbox chats ----

    [Fact]
    public void CreateGroupChat_Valid_Passes()
        => new CreateGroupChatCommandValidator().TestValidate(new CreateGroupChatCommand("Design team", Ids(3)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CreateGroupChat_LongNameOrTooManyMembers_Fails()
    {
        var result = new CreateGroupChatCommandValidator().TestValidate(new CreateGroupChatCommand(new string('n', 81), Ids(251)));
        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor(x => x.EmployeeIds);
    }

    [Fact]
    public void CreateGroupChat_BlankName_IsLeftToTheHandler()
        => new CreateGroupChatCommandValidator().TestValidate(new CreateGroupChatCommand("", Ids(1)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void RenameGroupChat_80Chars_Passes_81_Fails()
    {
        var validator = new RenameGroupChatCommandValidator();
        validator.TestValidate(new RenameGroupChatCommand(Guid.NewGuid(), new string('n', 80))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new RenameGroupChatCommand(Guid.NewGuid(), new string('n', 81))).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void SetChatDescription_NullClears_500Passes_501Fails()
    {
        var validator = new SetChatDescriptionCommandValidator();
        validator.TestValidate(new SetChatDescriptionCommand(Guid.NewGuid(), null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new SetChatDescriptionCommand(Guid.NewGuid(), new string('d', 500))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new SetChatDescriptionCommand(Guid.NewGuid(), new string('d', 501)))
            .ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void StartCaseGroup_Valid_Passes()
        => new StartCaseGroupCommandValidator()
            .TestValidate(new StartCaseGroupCommand("leave", "request", Guid.NewGuid(), Ids(2), "Leave chat"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void StartCaseGroup_MissingSubjectOrOversizedInput_Fails()
    {
        var result = new StartCaseGroupCommandValidator()
            .TestValidate(new StartCaseGroupCommand("", new string('k', 41), Guid.NewGuid(), Ids(251), new string('n', 81)));
        result.ShouldHaveValidationErrorFor(x => x.Source);
        result.ShouldHaveValidationErrorFor(x => x.Kind);
        result.ShouldHaveValidationErrorFor(x => x.EmployeeIds);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void ForwardCase_UpTo20Chats_Passes_More_Fails()
    {
        var validator = new ForwardCaseCommandValidator();
        validator.TestValidate(new ForwardCaseCommand("leave", "request", Guid.NewGuid(), Ids(20))).ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new ForwardCaseCommand(new string('s', 41), "request", Guid.NewGuid(), Ids(21)));
        result.ShouldHaveValidationErrorFor(x => x.ConversationIds);
        result.ShouldHaveValidationErrorFor(x => x.Source);
    }

    [Fact]
    public void ForwardMessage_UpTo20Chats_Passes_More_Fails()
    {
        var validator = new ForwardMessageCommandValidator();
        validator.TestValidate(new ForwardMessageCommand(Guid.NewGuid(), Ids(20))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ForwardMessageCommand(Guid.NewGuid(), Ids(21))).ShouldHaveValidationErrorFor(x => x.ConversationIds);
    }

    // ---- Task comments ----

    [Fact]
    public void CreateTaskComment_Valid_Passes()
        => new CreateTaskCommentCommandValidator()
            .TestValidate(new CreateTaskCommentCommand(Guid.NewGuid(), null, "<p>Looks <i>good</i></p>", Ids(20)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CreateTaskComment_ActiveContentTooLongOrTooManyFiles_Fails()
    {
        var validator = new CreateTaskCommentCommandValidator();
        validator.TestValidate(new CreateTaskCommentCommand(Guid.NewGuid(), null, "<div onclick=\"x()\">x</div>", []))
            .ShouldHaveValidationErrorFor(x => x.Content).WithErrorMessage(RichTextRules.ActiveContentError);
        validator.TestValidate(new CreateTaskCommentCommand(Guid.NewGuid(), null, new string('a', 50_001), []))
            .ShouldHaveValidationErrorFor(x => x.Content);
        validator.TestValidate(new CreateTaskCommentCommand(null, Guid.NewGuid(), "reply", Ids(21)))
            .ShouldHaveValidationErrorFor(x => x.AttachmentFileIds);
    }

    [Fact]
    public void EditTaskComment_Valid_Passes_ActiveContentOrTooLong_Fails()
    {
        var validator = new EditTaskCommentCommandValidator();
        validator.TestValidate(new EditTaskCommentCommand(Guid.NewGuid(), new string('a', 50_000), Ids(1))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new EditTaskCommentCommand(Guid.NewGuid(), Script, [])).ShouldHaveValidationErrorFor(x => x.Content);
        validator.TestValidate(new EditTaskCommentCommand(Guid.NewGuid(), new string('a', 50_001), [])).ShouldHaveValidationErrorFor(x => x.Content);
        validator.TestValidate(new EditTaskCommentCommand(Guid.NewGuid(), "ok", Ids(21))).ShouldHaveValidationErrorFor(x => x.AttachmentFileIds);
    }
}
