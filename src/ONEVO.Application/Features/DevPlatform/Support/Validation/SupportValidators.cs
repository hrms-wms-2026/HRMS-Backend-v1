using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.DevPlatform.Support.Admin;
using ONEVO.Application.Features.DevPlatform.Support.Announcements;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.DevPlatform.Support.Tenant;

namespace ONEVO.Application.Features.DevPlatform.Support.Validation;

/// <summary>
/// Task 19b. Column limits for support tickets, comments, attachments and announcements. SupportTicketRules and
/// AnnouncementRules still answer missing fields, unknown priorities, severities and audiences, and the date order,
/// with their own messages. These validators bound the sizes before the handler loads anything.
/// <list type="bullet">
/// <item>support_tickets: subject nvarchar(200), description nvarchar(10000), status and priority nvarchar(20),
/// category nvarchar(80).</item>
/// <item>support_ticket_comments.body is nvarchar(10000).</item>
/// <item>support_ticket_attachments: file_name nvarchar(255), content_type nvarchar(150).</item>
/// <item>platform_announcements: title nvarchar(200), body nvarchar(5000), severity nvarchar(20), audience
/// nvarchar(30).</item>
/// </list>
/// </summary>
public static class SupportLimits
{
    public const int StatusMaxLength = 20;
    public const int PriorityMaxLength = 20;
    public const int SeverityMaxLength = 20;
    public const int AudienceMaxLength = 30;

    /// <summary>An announcement window may be at most this long. Announcements are banners for maintenance and
    /// incidents. An open-ended one leaves <c>EndsAt</c> empty, which this rule allows.</summary>
    public static readonly TimeSpan AnnouncementMaxSpan = TimeSpan.FromDays(366);
}

/// <summary>The four ticket create and edit commands (admin and tenant) share these fields.</summary>
public interface ISupportTicketInput
{
    string? Subject { get; }
    string? Description { get; }
    string? Priority { get; }
    string? Category { get; }
}

public abstract class SupportTicketInputValidator<T> : AbstractValidator<T> where T : ISupportTicketInput
{
    protected SupportTicketInputValidator()
    {
        RuleFor(x => x.Subject).ValidRichText(SupportTicketRules.SubjectMaxLength);
        RuleFor(x => x.Description).ValidRichText(SupportTicketRules.DescriptionMaxLength);
        RuleFor(x => x.Priority).ValidName(SupportLimits.PriorityMaxLength, required: false);
        RuleFor(x => x.Category).ValidName(SupportTicketRules.CategoryMaxLength, required: false);
    }
}

public sealed class CreateSupportTicketCommandValidator : SupportTicketInputValidator<CreateSupportTicketCommand> { }

public sealed class UpdateSupportTicketCommandValidator : SupportTicketInputValidator<UpdateSupportTicketCommand> { }

public sealed class CreateTenantSupportTicketCommandValidator : SupportTicketInputValidator<CreateTenantSupportTicketCommand> { }

public sealed class UpdateTenantSupportTicketCommandValidator : SupportTicketInputValidator<UpdateTenantSupportTicketCommand> { }

public sealed class UpdateSupportTicketStatusCommandValidator : AbstractValidator<UpdateSupportTicketStatusCommand>
{
    public UpdateSupportTicketStatusCommandValidator()
    {
        RuleFor(x => x.Status).ValidName(SupportLimits.StatusMaxLength, required: false);
    }
}

public sealed class AddSupportTicketCommentCommandValidator : AbstractValidator<AddSupportTicketCommentCommand>
{
    public AddSupportTicketCommentCommandValidator()
    {
        RuleFor(x => x.Body).ValidRichText(SupportTicketRules.CommentMaxLength);
    }
}

public sealed class AddTenantSupportTicketCommentCommandValidator : AbstractValidator<AddTenantSupportTicketCommentCommand>
{
    public AddTenantSupportTicketCommentCommandValidator()
    {
        RuleFor(x => x.Body).ValidRichText(SupportTicketRules.CommentMaxLength);
    }
}

/// <summary>The upload's name and content type are copied into support_ticket_attachments. Missing values and
/// the allowed types are left to the upload purpose policy.</summary>
public sealed class SupportUploadValidator : AbstractValidator<SupportUpload>
{
    public SupportUploadValidator()
    {
        RuleFor(x => x.FileName).ValidName(InputLimits.FileNameMaxLength, required: false);
        RuleFor(x => x.ContentType).ValidName(InputLimits.ContentTypeMaxLength, required: false);
    }
}

public sealed class UploadSupportTicketAttachmentCommandValidator : AbstractValidator<UploadSupportTicketAttachmentCommand>
{
    public UploadSupportTicketAttachmentCommandValidator()
    {
        RuleFor(x => x.Upload).SetValidator(new SupportUploadValidator());
    }
}

public sealed class UploadTenantSupportTicketAttachmentCommandValidator : AbstractValidator<UploadTenantSupportTicketAttachmentCommand>
{
    public UploadTenantSupportTicketAttachmentCommandValidator()
    {
        RuleFor(x => x.Upload).SetValidator(new SupportUploadValidator());
    }
}

/// <summary>Both announcement commands share these fields.</summary>
public interface IPlatformAnnouncementInput
{
    string? Title { get; }
    string? Body { get; }
    string? Severity { get; }
    string? Audience { get; }
    DateTimeOffset? StartsAt { get; }
    DateTimeOffset? EndsAt { get; }
}

public abstract class PlatformAnnouncementInputValidator<T> : AbstractValidator<T> where T : IPlatformAnnouncementInput
{
    protected PlatformAnnouncementInputValidator()
    {
        RuleFor(x => x.Title).ValidRichText(SupportTicketRules.AnnouncementTitleMaxLength);
        RuleFor(x => x.Body).ValidRichText(SupportTicketRules.AnnouncementBodyMaxLength);
        RuleFor(x => x.Severity).ValidName(SupportLimits.SeverityMaxLength, required: false);
        RuleFor(x => x.Audience).ValidName(SupportLimits.AudienceMaxLength, required: false);
        RuleFor(x => x).ValidDateRange(x => x.StartsAt, x => x.EndsAt, SupportLimits.AnnouncementMaxSpan);
    }
}

public sealed class CreatePlatformAnnouncementCommandValidator : PlatformAnnouncementInputValidator<CreatePlatformAnnouncementCommand> { }

public sealed class UpdatePlatformAnnouncementCommandValidator : PlatformAnnouncementInputValidator<UpdatePlatformAnnouncementCommand> { }
