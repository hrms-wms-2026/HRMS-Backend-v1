using System.Text.Json;
using FluentAssertions;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.ResetPlatformUserMfa;
using ONEVO.Application.Features.DevPlatform.Support.Admin;
using ONEVO.Application.Features.DevPlatform.Support.Announcements;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.DevPlatform.Support.Tenant;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Auditing;

/// <summary>
/// Security audit PA-SEC-001: support, announcement and MFA-reset commands are audited, and their audit rows
/// never carry ticket text, comment text, announcement bodies or attachment file names.
/// </summary>
public sealed class SupportAuditProjectionTests
{
    private const string Text = "PRIVATE_TICKET_TEXT_6c1f";
    private const string FileName = "PRIVATE_FILE_NAME_payslip.pdf";
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SupportUpload Upload() => new(Stream.Null, FileName, "application/pdf", 1234);

    private static AuditEntry Describe(object command)
    {
        var audited = command.GetType().GetInterfaces()
            .SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuditedCommand<>));
        audited.Should().NotBeNull($"{command.GetType().Name} must implement IAuditedCommand<TResponse>");

        return (AuditEntry)audited!.GetMethod("DescribeAudit")!.Invoke(command, [null])!;
    }

    public static TheoryData<object, string> Commands() => new()
    {
        { new CreateSupportTicketCommand(Id, Text, Text, "high", Text, Id), "support_ticket.created" },
        { new UpdateSupportTicketCommand(Id, Text, Text, "low", Text), "support_ticket.updated" },
        { new UpdateSupportTicketStatusCommand(Id, "resolved"), "support_ticket.status_changed" },
        { new AssignSupportTicketCommand(Id, Id), "support_ticket.assigned" },
        { new AddSupportTicketCommentCommand(Id, Text, true, Id), "support_ticket.commented" },
        { new UploadSupportTicketAttachmentCommand(Id, Upload(), false, Id), "support_ticket.attachment_added" },
        { new CreatePlatformAnnouncementCommand(Text, Text, "info", "all", null, null, Id), "announcement.created" },
        { new UpdatePlatformAnnouncementCommand(Id, Text, Text, "info", "all", null, null), "announcement.updated" },
        { new SetPlatformAnnouncementPublishedCommand(Id, true, Id), "announcement.published" },
        { new DeletePlatformAnnouncementCommand(Id), "announcement.deleted" },
        { new CreateTenantSupportTicketCommand(Text, Text, "high", Text), "support_ticket.created" },
        { new UpdateTenantSupportTicketCommand(Id, Text, Text, "low", Text), "support_ticket.updated" },
        { new AddTenantSupportTicketCommentCommand(Id, Text), "support_ticket.commented" },
        { new UploadTenantSupportTicketAttachmentCommand(Id, Upload()), "support_ticket.attachment_added" },
        { new ResetPlatformUserMfaCommand(Id), "platform_user.mfa_reset" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public void Audit_row_has_the_action_and_no_user_text_or_file_name(object command, string action)
    {
        var entry = Describe(command);
        var details = JsonSerializer.Serialize(entry.Details);

        entry.Action.Should().Be(action);
        details.Should().NotContain(Text).And.NotContain(FileName);
    }

    [Fact]
    public void Unpublishing_is_audited_as_its_own_action()
    {
        var entry = Describe(new SetPlatformAnnouncementPublishedCommand(Id, false, Id));

        entry.Action.Should().Be("announcement.unpublished");
    }
}
