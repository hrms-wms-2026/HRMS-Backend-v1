using FluentValidation.TestHelper;
using ONEVO.Application.Features.DevPlatform.Support.Admin;
using ONEVO.Application.Features.DevPlatform.Support.Announcements;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.DevPlatform.Support.Tenant;
using ONEVO.Application.Features.DevPlatform.Support.Validation;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: DevPlatform support tickets, comments, attachments and announcements.</summary>
public sealed class SupportValidatorTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Ticket_AtColumnLimits_Passes()
    {
        new CreateSupportTicketCommandValidator()
            .TestValidate(new CreateSupportTicketCommand(null, S(200), S(10_000), "urgent", S(80), Actor))
            .ShouldNotHaveAnyValidationErrors();
        new UpdateTenantSupportTicketCommandValidator()
            .TestValidate(new UpdateTenantSupportTicketCommand(Guid.NewGuid(), "Printer", "<p>Broken</p>", "low", null))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ticket_MissingFields_AreLeftToTheHandler()
        => new CreateTenantSupportTicketCommandValidator()
            .TestValidate(new CreateTenantSupportTicketCommand(null, null, null, null))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Ticket_OverLimitsOrActiveContent_Fails()
    {
        var result = new UpdateSupportTicketCommandValidator()
            .TestValidate(new UpdateSupportTicketCommand(Guid.NewGuid(), S(201), S(10_001), S(21), S(81)));
        result.ShouldHaveValidationErrorFor(x => x.Subject);
        result.ShouldHaveValidationErrorFor(x => x.Description);
        result.ShouldHaveValidationErrorFor(x => x.Priority);
        result.ShouldHaveValidationErrorFor(x => x.Category);

        new CreateTenantSupportTicketCommandValidator()
            .TestValidate(new CreateTenantSupportTicketCommand("ok", Script, "low", null))
            .ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Status_UpTo20_Passes_Longer_Fails()
    {
        var validator = new UpdateSupportTicketStatusCommandValidator();
        validator.TestValidate(new UpdateSupportTicketStatusCommand(Guid.NewGuid(), "resolved")).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new UpdateSupportTicketStatusCommand(Guid.NewGuid(), S(21))).ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Comments_BoundLengthAndActiveContent()
    {
        var admin = new AddSupportTicketCommentCommandValidator();
        admin.TestValidate(new AddSupportTicketCommentCommand(Guid.NewGuid(), S(10_000), true, Actor)).ShouldNotHaveAnyValidationErrors();
        admin.TestValidate(new AddSupportTicketCommentCommand(Guid.NewGuid(), S(10_001), true, Actor)).ShouldHaveValidationErrorFor(x => x.Body);

        var tenant = new AddTenantSupportTicketCommentCommandValidator();
        tenant.TestValidate(new AddTenantSupportTicketCommentCommand(Guid.NewGuid(), "<b>thanks</b>")).ShouldNotHaveAnyValidationErrors();
        tenant.TestValidate(new AddTenantSupportTicketCommentCommand(Guid.NewGuid(), Script)).ShouldHaveValidationErrorFor(x => x.Body);
    }

    [Fact]
    public void Attachments_BoundFileNameAndContentType()
    {
        var ok = new SupportUpload(EmptyStream(), S(251) + ".png", "image/png", 10);
        new UploadSupportTicketAttachmentCommandValidator()
            .TestValidate(new UploadSupportTicketAttachmentCommand(Guid.NewGuid(), ok, false, Actor)).ShouldNotHaveAnyValidationErrors();

        var bad = new SupportUpload(EmptyStream(), S(256), S(151), 10);
        var result = new UploadTenantSupportTicketAttachmentCommandValidator()
            .TestValidate(new UploadTenantSupportTicketAttachmentCommand(Guid.NewGuid(), bad));
        result.ShouldHaveValidationErrorFor("Upload.FileName");
        result.ShouldHaveValidationErrorFor("Upload.ContentType");
    }

    [Fact]
    public void Announcement_AtLimits_Passes()
    {
        var start = DateTimeOffset.UtcNow;
        new CreatePlatformAnnouncementCommandValidator()
            .TestValidate(new CreatePlatformAnnouncementCommand(S(200), S(5_000), "critical", "platform_admins", start, start.AddDays(366), Actor))
            .ShouldNotHaveAnyValidationErrors();
        new UpdatePlatformAnnouncementCommandValidator()
            .TestValidate(new UpdatePlatformAnnouncementCommand(Guid.NewGuid(), "Maintenance", "Tonight", "info", "all", null, null))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Announcement_OverLimits_ActiveContent_OrLongWindow_Fails()
    {
        var start = DateTimeOffset.UtcNow;
        var result = new UpdatePlatformAnnouncementCommandValidator()
            .TestValidate(new UpdatePlatformAnnouncementCommand(Guid.NewGuid(), S(201), Script, S(21), S(31), start, start.AddDays(367)));
        result.ShouldHaveValidationErrorFor(x => x.Title);
        result.ShouldHaveValidationErrorFor(x => x.Body);
        result.ShouldHaveValidationErrorFor(x => x.Severity);
        result.ShouldHaveValidationErrorFor(x => x.Audience);
        result.ShouldHaveValidationErrorFor(x => x.EndsAt);
    }
}
