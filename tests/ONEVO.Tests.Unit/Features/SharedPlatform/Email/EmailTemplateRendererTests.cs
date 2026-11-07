using FluentAssertions;
using Microsoft.Extensions.Options;
using ONEVO.Infrastructure.ExternalServices.Email;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Email;

public sealed class EmailTemplateRendererTests
{
    [Fact]
    public void RenderPasswordReset_WithoutTenantSlug_UsesFlatAppBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render("password_reset", new { reset_token = "tok-123" });

        rendered.HtmlBody.Should().Contain("http://localhost:5173/auth/reset-password?token=tok-123");
        rendered.TextBody.Should().Contain("http://localhost:5173/auth/reset-password?token=tok-123");
    }

    [Fact]
    public void RenderPasswordReset_WithTenantSlug_PrefixesSlugOntoAppBaseUrlHost()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render(
            "password_reset",
            new { reset_token = "tok-123", tenant_slug = "acme" });

        rendered.TextBody.Should().Contain("http://acme.localhost:5173/auth/reset-password?token=tok-123");
    }

    [Fact]
    public void RenderPasswordReset_WithTenantSlug_PreservesSchemeAndPortOnProductionStyleHost()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "https://onevo.io"
        }));

        var rendered = renderer.Render(
            "password_reset",
            new { reset_token = "tok-123", tenant_slug = "acme" });

        rendered.TextBody.Should().Contain("https://acme.onevo.io/auth/reset-password?token=tok-123");
    }

    [Fact]
    public void RenderPasswordReset_WithEmptyAppBaseUrl_FallsBackToPlaceholderRegardlessOfTenantSlug()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = string.Empty
        }));

        var rendered = renderer.Render(
            "password_reset",
            new { reset_token = "tok-123", tenant_slug = "acme" });

        rendered.TextBody.Should().Contain("token=tok-123");
        rendered.TextBody.Should().Contain("placeholder");
    }

    [Fact]
    public void RenderPasswordReset_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render("password_reset", new { reset_token = "ab+c/d==" });

        rendered.TextBody.Should().Contain("token=ab%2Bc%2Fd%3D%3D");
        rendered.TextBody.Should().NotContain("token=ab+c/d==");
    }

    [Fact]
    public void RenderAdminPasswordReset_UsesAdminConsoleBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "http://localhost:5174"
        }));

        var rendered = renderer.Render("admin_password_reset", new { reset_token = "tok-abc" });

        rendered.HtmlBody.Should().Contain("http://localhost:5174/auth/reset-password?token=tok-abc");
        rendered.TextBody.Should().Contain("http://localhost:5174/auth/reset-password?token=tok-abc");
    }

    [Fact]
    public void RenderAdminPasswordReset_WithEmptyAdminConsoleBaseUrl_FallsBackToPlaceholder()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = string.Empty
        }));

        var rendered = renderer.Render("admin_password_reset", new { reset_token = "tok-abc" });

        rendered.TextBody.Should().Contain("token=tok-abc");
        rendered.TextBody.Should().Contain("placeholder");
    }

    [Fact]
    public void RenderAdminPasswordReset_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "http://localhost:5174"
        }));

        var rendered = renderer.Render("admin_password_reset", new { reset_token = "ab+c/d==" });

        rendered.TextBody.Should().Contain("token=ab%2Bc%2Fd%3D%3D");
        rendered.TextBody.Should().NotContain("token=ab+c/d==");
    }

    [Fact]
    public void RenderPlatformManagerInvite_UsesAdminConsoleBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "https://admin.localhost:4200"
        }));

        var rendered = renderer.Render("platform_manager_invite", new
        {
            full_name = "New Manager",
            invite_token = "tok-xyz"
        });

        rendered.HtmlBody.Should().Contain("https://admin.localhost:4200/auth/accept-invite?token=tok-xyz");
        rendered.TextBody.Should().Contain("https://admin.localhost:4200/auth/accept-invite?token=tok-xyz");
    }

    [Fact]
    public void RenderPlatformManagerInvite_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "https://admin.localhost:4200"
        }));

        var rendered = renderer.Render("platform_manager_invite", new
        {
            full_name = "New Manager",
            invite_token = "ab+c/d=="
        });

        rendered.TextBody.Should().Contain("token=ab%2Bc%2Fd%3D%3D");
        rendered.TextBody.Should().NotContain("token=ab+c/d==");
    }

    [Fact]
    public void RenderEmployeeOnboardingInvite_UsesAppBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render("employee_onboarding_invite", new
        {
            first_name = "Ada",
            last_name = "Lovelace",
            invite_token = "tok-xyz"
        });

        rendered.HtmlBody.Should().Contain("http://localhost:5173/auth/invitations/tok-xyz");
        rendered.TextBody.Should().Contain("http://localhost:5173/auth/invitations/tok-xyz");
        rendered.TextBody.Should().Contain("Ada");
        rendered.Subject.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RenderEmployeeOnboardingInvite_WithTenantSlug_PrefixesSlugOntoAppBaseUrlHost()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "https://localhost:4200"
        }));

        var rendered = renderer.Render("employee_onboarding_invite", new
        {
            first_name = "Ada",
            last_name = "Lovelace",
            invite_token = "tok-xyz",
            tenant_slug = "dapi"
        });

        rendered.HtmlBody.Should().Contain("https://dapi.localhost:4200/auth/invitations/tok-xyz");
        rendered.TextBody.Should().Contain("https://dapi.localhost:4200/auth/invitations/tok-xyz");
    }

    [Fact]
    public void RenderEmployeeOnboardingInvite_StatesA24HourValidityWindow()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render("employee_onboarding_invite", new
        {
            first_name = "Ada",
            last_name = "Lovelace",
            invite_token = "tok-xyz"
        });

        rendered.HtmlBody.Should().Contain("24 hours");
        rendered.TextBody.Should().Contain("24 hours");
        rendered.HtmlBody.Should().NotContain("72 hours");
        rendered.TextBody.Should().NotContain("72 hours");
    }

    [Fact]
    public void RenderEmployeeOnboardingInvite_WithEmptyAppBaseUrl_FallsBackToPlaceholder()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = string.Empty
        }));

        var rendered = renderer.Render("employee_onboarding_invite", new
        {
            first_name = "Ada",
            last_name = "Lovelace",
            invite_token = "tok-xyz"
        });

        rendered.TextBody.Should().Contain("token=tok-xyz");
        rendered.TextBody.Should().Contain("placeholder");
    }

    [Fact]
    public void RenderEmployeeOnboardingInvite_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "http://localhost:5173"
        }));

        var rendered = renderer.Render("employee_onboarding_invite", new
        {
            first_name = "Ada",
            last_name = "Lovelace",
            invite_token = "ab+c/d=="
        });

        // employee_onboarding_invite embeds the token as a URL path segment
        // (/auth/invitations/{token}), not a query string, so a legacy Base64 token still
        // percent-encodes safely here rather than breaking the route.
        rendered.TextBody.Should().Contain("http://localhost:5173/auth/invitations/ab%2Bc%2Fd%3D%3D");
        rendered.TextBody.Should().NotContain("ab+c/d==");
    }

    [Fact]
    public void RenderAdminPasswordChanged_ProducesSecurityNoticeCopy()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("admin_password_changed", new { });

        rendered.HtmlBody.Should().Contain("password was changed");
        rendered.TextBody.Should().Contain("If this wasn't you", "must tell the recipient how to react if this wasn't them");
    }

    [Fact]
    public void RenderInvoiceEmail_IncludesInvoiceNumberTenantNameAmountAndDueDate()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("invoice_email", new
        {
            tenant_name = "Acme Co",
            invoice_number = "INV-1001",
            status = "open",
            currency = "USD",
            subtotal_amount = 100m,
            tax_amount = 10m,
            discount_amount = 0m,
            total_amount = 110m,
            period_start = "2026-08-01",
            period_end = "2026-08-31",
            due_at = "2026-08-20 00:00:00Z",
            paid_at = (string?)null,
            is_receipt = false
        });

        rendered.Subject.Should().Contain("INV-1001");
        rendered.Subject.Should().Contain("Acme Co");
        rendered.HtmlBody.Should().Contain("INV-1001");
        rendered.HtmlBody.Should().Contain("Acme Co");
        rendered.HtmlBody.Should().Contain("USD");
        rendered.HtmlBody.Should().Contain("110");
        rendered.HtmlBody.Should().Contain("2026-08-20 00:00:00Z");
    }

    [Fact]
    public void RenderInvoiceEmail_PaidStatus_UsesReceiptSubject()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("invoice_email", new
        {
            tenant_name = "Acme Co",
            invoice_number = "INV-2002",
            status = "paid",
            currency = "USD",
            subtotal_amount = 50m,
            tax_amount = 0m,
            discount_amount = 0m,
            total_amount = 50m,
            period_start = "2026-08-01",
            period_end = "2026-08-31",
            due_at = (string?)null,
            paid_at = "2026-08-13 12:00:00Z",
            is_receipt = true
        });

        rendered.Subject.Should().Contain("Payment receipt");
        rendered.HtmlBody.Should().Contain("Paid on");
    }

    [Fact]
    public void RenderPositionChangeApprovalRequest_WithTenantSlug_PrefixesSlugOntoAppBaseUrlHost()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AppBaseUrl = "https://localhost:4200"
        }));

        var rendered = renderer.Render("position_change_approval_request", new
        {
            employeeName = "Ada Lovelace",
            positionName = "CFO",
            changeReason = "promotion",
            tenant_slug = "dapi"
        });

        rendered.HtmlBody.Should().Contain("https://dapi.localhost:4200/people/approvals");
        rendered.TextBody.Should().Contain("https://dapi.localhost:4200/people/approvals");
    }

    [Fact]
    public void RenderCalendarEventInvite_FormatsTheStartTimeAsAReadableDate_NotRawIso()
    {
        // startDateUtc always arrives as an ISO 8601 string here (SendTemplateAsync round-trips its
        // anonymous object through JSON before Render() ever sees it), so the raw "2026-10-05T09:30:00+00:00"
        // that reached real recipients' inboxes must be parsed and reformatted, not passed through as-is.
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Dapi Owner",
            eventTitle = "test 404",
            startDateUtc = "2026-10-05T09:30:00+00:00",
            location = (string?)null,
            organizerName = "Dapi Owner"
        });

        rendered.HtmlBody.Should().NotContain("2026-10-05T09:30:00");
        rendered.TextBody.Should().NotContain("2026-10-05T09:30:00");
        rendered.HtmlBody.Should().Contain("Monday, October 5, 2026 at 9:30 AM UTC");
        rendered.TextBody.Should().Contain("Monday, October 5, 2026 at 9:30 AM UTC");
    }

    [Fact]
    public void RenderCalendarEvent_ShowsTheStartInTheEventsTimeZone()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Guest",
            eventTitle = "Sync",
            startDateUtc = "2026-11-06T12:15:00+00:00",
            timeZoneId = "Asia/Colombo",
            organizerName = "Dapi Owner"
        });

        rendered.HtmlBody.Should().Contain("Friday, November 6, 2026 at 5:45 PM (Asia/Colombo)").And.NotContain("UTC");
        rendered.TextBody.Should().Contain("Friday, November 6, 2026 at 5:45 PM (Asia/Colombo)").And.NotContain("UTC");
    }

    private static RenderedEmail RenderInvite(string template = "calendar_event_invite", string? endDateUtc = "2026-11-06T13:45:00+00:00",
        string title = "Design review", string? location = null, string? meetingLink = null, string? recurrenceRule = null, string? timeZoneId = null)
        => new EmailTemplateRenderer(Options.Create(new EmailOptions())).Render(template, new
        {
            recipientName = "Guest",
            eventTitle = title,
            startDateUtc = "2026-11-06T12:15:00+00:00",
            endDateUtc,
            location,
            meetingLink,
            recurrenceRule,
            timeZoneId,
            organizerName = "Dapi Owner"
        });

    [Fact]
    public void RenderCalendarEvent_AnInvite_OffersGoogleAndOutlookLinksForTheEventsTime()
    {
        var rendered = RenderInvite();

        rendered.TextBody.Should().Contain(
            "https://calendar.google.com/calendar/render?action=TEMPLATE&text=Design%20review&dates=20261106T121500Z/20261106T134500Z");
        rendered.TextBody.Should().Contain(
            "https://outlook.office.com/calendar/action/compose?rru=addevent&subject=Design%20review&startdt=2026-11-06T12%3A15%3A00Z&enddt=2026-11-06T13%3A45%3A00Z");
        // The & in a link's query is an HTML entity inside the href.
        rendered.HtmlBody.Should().Contain("Add to Google Calendar").And.Contain("Add to Outlook")
            .And.Contain("render?action=TEMPLATE&amp;text=Design%20review");
    }

    [Fact]
    public void RenderCalendarEvent_TheLinksCarryTheLocationAndTheJoinLink()
    {
        var rendered = RenderInvite(location: "Room 1", meetingLink: "https://zoom.us/j/1?pwd=a&b=c");

        const string details = "Join%3A%20https%3A%2F%2Fzoom.us%2Fj%2F1%3Fpwd%3Da%26b%3Dc";
        rendered.TextBody.Should().Contain($"&details={details}&location=Room%201");
        rendered.TextBody.Should().Contain($"&body={details}&location=Room%201");
    }

    [Fact]
    public void RenderCalendarEvent_AnUnsafeTitle_IsEncodedInTheLinks()
    {
        var rendered = RenderInvite(title: "A&B \"<script>\"");

        rendered.TextBody.Should().Contain("&text=A%26B%20%22%3Cscript%3E%22&");
        rendered.HtmlBody.Should().NotContain("<script>");
    }

    [Theory]
    [InlineData("calendar_event_updated")]
    [InlineData("calendar_event_cancelled")]
    [InlineData("calendar_event_removed")]
    [InlineData("calendar_event_link_removed")]
    public void RenderCalendarEvent_OnlyAnInvite_OffersTheLinks(string template)
    {
        // Someone who already added the event would get a second copy from an update's link.
        var rendered = RenderInvite(template);

        rendered.HtmlBody.Should().NotContain("calendar.google.com").And.NotContain("outlook.office.com");
        rendered.TextBody.Should().NotContain("calendar.google.com").And.NotContain("outlook.office.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026-11-06T11:00:00+00:00")]
    public void RenderCalendarEvent_WithoutAUsableEndTime_OffersNoLinks(string? endDateUtc)
    {
        var rendered = RenderInvite(endDateUtc: endDateUtc);

        rendered.HtmlBody.Should().NotContain("calendar.google.com").And.NotContain("outlook.office.com");
    }

    [Fact]
    public void RenderCalendarEvent_ARepeatingEvent_OffersOnlyGoogleWithItsRule()
    {
        // Outlook's compose link cannot repeat, so it would add a single meeting of a series.
        var rendered = RenderInvite(recurrenceRule: "FREQ=WEEKLY;BYDAY=FR", timeZoneId: "Asia/Colombo");

        rendered.TextBody.Should().Contain("&recur=RRULE%3AFREQ%3DWEEKLY%3BBYDAY%3DFR&ctz=Asia%2FColombo");
        rendered.TextBody.Should().NotContain("outlook.office.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void RenderCalendarEvent_WithoutAKnownTimeZone_StaysInUtc(string? timeZoneId)
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Guest",
            eventTitle = "Sync",
            startDateUtc = "2026-11-06T12:15:00+00:00",
            timeZoneId,
            organizerName = "Dapi Owner"
        });

        rendered.HtmlBody.Should().Contain("Friday, November 6, 2026 at 12:15 PM UTC");
    }

    [Fact]
    public void RenderCalendarEventInvite_WithMeetingLink_IncludesJoinUrlInBothBodies()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Ada Lovelace",
            eventTitle = "Deployment Meeting",
            startDateUtc = "2026-09-29T14:45:00Z",
            location = (string?)null,
            organizerName = "Dapi Owner",
            meetingLink = "https://us05web.zoom.us/j/123456789"
        });

        rendered.HtmlBody.Should().Contain("https://us05web.zoom.us/j/123456789");
        rendered.TextBody.Should().Contain("https://us05web.zoom.us/j/123456789");
    }

    [Fact]
    public void RenderCalendarEventInvite_WithoutMeetingLink_OmitsJoinLinkLine()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Ada Lovelace",
            eventTitle = "Deployment Meeting",
            startDateUtc = "2026-09-29T14:45:00Z",
            location = (string?)null,
            organizerName = "Dapi Owner"
        });

        rendered.HtmlBody.Should().NotContain("Join meeting");
        rendered.TextBody.Should().NotContain("Join:");
    }

    [Theory]
    [InlineData("calendar_event_updated", "Updated: Deployment Meeting", "has updated")]
    [InlineData("calendar_event_cancelled", "Cancelled: Deployment Meeting", "has cancelled")]
    [InlineData("calendar_event_removed", "Removed: Deployment Meeting", "removed you from")]
    public void RenderCalendarEventChange_SaysWhatHappenedInSubjectAndBothBodies(string template, string subject, string phrase)
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render(template, new
        {
            recipientName = "Ada Lovelace",
            eventTitle = "Deployment Meeting",
            startDateUtc = "2026-09-29T14:45:00Z",
            location = "Room 4",
            organizerName = "Dapi Owner",
            meetingLink = "https://us05web.zoom.us/j/123456789"
        });

        rendered.Subject.Should().Be(subject);
        rendered.HtmlBody.Should().Contain(phrase).And.Contain("Tuesday, September 29, 2026 at 2:45 PM UTC").And.Contain("Room 4");
        rendered.TextBody.Should().Contain(phrase);
        // A cancelled or removed event offers no join link.
        if (template == "calendar_event_updated")
            rendered.HtmlBody.Should().Contain("https://us05web.zoom.us/j/123456789");
        else
            rendered.HtmlBody.Should().NotContain("https://us05web.zoom.us/j/123456789");
    }

    [Fact]
    public void RenderCalendarEventInvite_LinkRemoved_SaysSo_WithoutTheLink()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_link_removed", new
        {
            recipientName = "Ada", eventTitle = "Standup", startDateUtc = "2026-09-29T14:45:00Z",
            location = (string?)null, organizerName = "Owner", meetingLink = "https://zoom.us/j/1"
        });

        rendered.Subject.Should().Be("Meeting link removed: Standup");
        rendered.HtmlBody.Should().NotContain("https://zoom.us/j/1");
    }
}
