using FluentValidation.TestHelper;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;
using ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;

namespace ONEVO.Tests.Unit.Features.Validation;

/// <summary>Task 19: date ranges (attendance corrections, backdated leave, recurring occurrences) and money (Stripe).</summary>
public sealed class DateAndMoneyValidatorTests
{
    private static readonly DateOnly Day = new(2026, 11, 17);
    private static readonly DateTimeOffset NineAm = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    // ---- Attendance corrections ----

    private static RequestAttendanceCorrectionCommand Request(
        string type = "full_day", DateTimeOffset? clockIn = null, DateTimeOffset? clockOut = null,
        IReadOnlyList<AttendanceCorrectionInputBreak>? breaks = null, string reason = "Forgot to clock in", string? notes = null)
        => new(Day, type, clockIn ?? NineAm, clockOut ?? NineAm.AddHours(8), breaks, reason, notes);

    private static AttendanceCorrectionInputBreak Break(DateTimeOffset start, DateTimeOffset end, string type = "lunch") => new(start, end, type);

    [Fact]
    public void RequestCorrection_Valid_Passes()
        => new RequestAttendanceCorrectionCommandValidator()
            .TestValidate(Request(breaks: [Break(NineAm.AddHours(3), NineAm.AddHours(4))], notes: new string('n', 2000)))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RequestCorrection_BlankReason_Fails(string reason)
        => new RequestAttendanceCorrectionCommandValidator().TestValidate(Request(reason: reason))
            .ShouldHaveValidationErrorFor(x => x.Reason);

    [Fact]
    public void RequestCorrection_OverColumnSizes_Fails()
    {
        var result = new RequestAttendanceCorrectionCommandValidator()
            .TestValidate(Request(type: new string('t', 31), reason: new string('r', 256), notes: new string('n', 2001)));
        result.ShouldHaveValidationErrorFor(x => x.CorrectionType);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void RequestCorrection_ClockOutBeforeClockIn_Fails()
        => new RequestAttendanceCorrectionCommandValidator()
            .TestValidate(Request(clockIn: NineAm, clockOut: NineAm.AddMinutes(-5)))
            .ShouldHaveValidationErrorFor("RequestedClockOutAt");

    [Fact]
    public void RequestCorrection_RangeLongerThan25Hours_Fails()
        => new RequestAttendanceCorrectionCommandValidator()
            .TestValidate(Request(clockIn: NineAm, clockOut: NineAm.AddHours(25).AddMinutes(1)))
            .ShouldHaveValidationErrorFor("RequestedClockOutAt");

    [Fact]
    public void RequestCorrection_TooManyBreaksOrBadBreak_Fails()
    {
        var many = Enumerable.Range(0, 21).Select(i => Break(NineAm.AddMinutes(i * 10), NineAm.AddMinutes(i * 10 + 5))).ToList();
        new RequestAttendanceCorrectionCommandValidator().TestValidate(Request(type: "break", breaks: many))
            .ShouldHaveValidationErrorFor(x => x.RequestedBreaks);

        var result = new RequestAttendanceCorrectionCommandValidator().TestValidate(
            Request(type: "break", breaks: [Break(NineAm.AddHours(2), NineAm.AddHours(1), new string('b', 31))]));
        result.ShouldHaveValidationErrorFor("RequestedBreaks[0].BreakType");
        result.ShouldHaveValidationErrorFor("RequestedBreaks[0].BreakEnd");
    }

    [Fact]
    public void PreviewCorrection_SameRulesAsRequest()
    {
        var validator = new PreviewAttendanceCorrectionCommandValidator();
        validator.TestValidate(new PreviewAttendanceCorrectionCommand(Day, "clock_in", NineAm, null, null, "Late badge", null))
            .ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new PreviewAttendanceCorrectionCommand(
            Day, "full_day", NineAm, NineAm.AddDays(2), null, new string('r', 256), null));
        result.ShouldHaveValidationErrorFor(x => x.Reason);
        result.ShouldHaveValidationErrorFor("RequestedClockOutAt");
    }

    [Fact]
    public void ApproveCorrection_CommentAndEditedValuesAreBounded()
    {
        var validator = new ApproveAttendanceCorrectionCommandValidator();
        validator.TestValidate(new ApproveAttendanceCorrectionCommand(Guid.NewGuid(), null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ApproveAttendanceCorrectionCommand(Guid.NewGuid(), "ok",
                new AttendanceCorrectionEditedValues(NineAm, NineAm.AddHours(8), [Break(NineAm.AddHours(3), NineAm.AddHours(4))])))
            .ShouldNotHaveAnyValidationErrors();

        validator.TestValidate(new ApproveAttendanceCorrectionCommand(Guid.NewGuid(), new string('c', 2001)))
            .ShouldHaveValidationErrorFor(x => x.ReviewComment);
        validator.TestValidate(new ApproveAttendanceCorrectionCommand(Guid.NewGuid(), null,
                new AttendanceCorrectionEditedValues(NineAm, NineAm.AddHours(-1), null)))
            .ShouldHaveValidationErrorFor("EditedValues.ClockOutAt");
    }

    [Fact]
    public void RejectCorrection_CommentUpTo2000_Passes_Longer_Fails()
    {
        var validator = new RejectAttendanceCorrectionCommandValidator();
        validator.TestValidate(new RejectAttendanceCorrectionCommand(Guid.NewGuid(), new string('c', 2000))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new RejectAttendanceCorrectionCommand(Guid.NewGuid(), new string('c', 2001)))
            .ShouldHaveValidationErrorFor(x => x.ReviewComment);
    }

    // ---- Approver backdated leave ----

    private static CreateApproverBackdatedLeaveCommandValidator BackdatedValidator(DateOnly today)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.Today).Returns(today);
        return new CreateApproverBackdatedLeaveCommandValidator(clock.Object);
    }

    [Fact]
    public void BackdatedLeave_WithinAYear_Passes()
        => BackdatedValidator(Day).TestValidate(new CreateApproverBackdatedLeaveCommand(Guid.NewGuid(), Guid.NewGuid(), Day.AddDays(-366), "Sick"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void BackdatedLeave_OlderThan366Days_Fails()
        => BackdatedValidator(Day).TestValidate(new CreateApproverBackdatedLeaveCommand(Guid.NewGuid(), Guid.NewGuid(), Day.AddDays(-367)))
            .ShouldHaveValidationErrorFor(x => x.WorkDate);

    [Fact]
    public void BackdatedLeave_ReasonOver2000_Fails()
        => BackdatedValidator(Day).TestValidate(new CreateApproverBackdatedLeaveCommand(Guid.NewGuid(), Guid.NewGuid(), Day.AddDays(-1), new string('r', 2001)))
            .ShouldHaveValidationErrorFor(x => x.Reason);

    // ---- Recurring calendar occurrence ----

    private static EditRecurringOccurrenceCommand Occurrence(
        string title = "Standup", string? description = null, DateTimeOffset? end = null,
        string? location = null, string? link = null, string? color = "#1a2b3c")
        => new(Guid.NewGuid(), NineAm, RecurrenceEditScope.ThisEventOnly, title, description, NineAm, end ?? NineAm.AddMinutes(15),
            false, location, link, color);

    [Fact]
    public void EditOccurrence_Valid_Passes()
        => new EditRecurringOccurrenceCommandValidator().TestValidate(Occurrence(description: new string('d', 10_000)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EditOccurrence_MissingTitle_Fails()
        => new EditRecurringOccurrenceCommandValidator().TestValidate(Occurrence(title: " "))
            .ShouldHaveValidationErrorFor(x => x.Title);

    [Fact]
    public void EditOccurrence_OverColumnSizes_Fails()
    {
        var result = new EditRecurringOccurrenceCommandValidator().TestValidate(Occurrence(
            title: new string('t', 201), description: new string('d', 10_001), location: new string('l', 501),
            link: "https://x.test/" + new string('l', 490), color: "#1a2b3c4"));
        result.ShouldHaveValidationErrorFor(x => x.Title);
        result.ShouldHaveValidationErrorFor(x => x.Description);
        result.ShouldHaveValidationErrorFor(x => x.Location);
        result.ShouldHaveValidationErrorFor(x => x.MeetingLink);
        result.ShouldHaveValidationErrorFor(x => x.Color);
    }

    [Fact]
    public void EditOccurrence_EndBeforeStartOrLongerThan366Days_Fails()
    {
        var validator = new EditRecurringOccurrenceCommandValidator();
        validator.TestValidate(Occurrence(end: NineAm.AddMinutes(-1))).ShouldHaveValidationErrorFor("EndDate");
        validator.TestValidate(Occurrence(end: NineAm.AddDays(367))).ShouldHaveValidationErrorFor("EndDate");
        validator.TestValidate(Occurrence(end: NineAm.AddDays(366))).ShouldNotHaveValidationErrorFor("EndDate");
    }

    // ---- Stripe webhook ----

    private static ProcessStripeEventCommand Invoice(
        decimal? total = 120.50m, string? currency = "usd", DateTimeOffset? periodEnd = null, string? invoiceNumber = "INV-0001")
        => new("invoice.payment_succeeded", "sub_123", "cus_123", null, NineAm, periodEnd ?? NineAm.AddMonths(1), null,
            ExternalInvoiceId: "in_123", InvoiceNumber: invoiceNumber, GatewayProvider: "stripe", Currency: currency,
            SubtotalAmount: 100m, TaxAmount: 20.50m, DiscountAmount: 0m, TotalAmount: total,
            IssuedAt: NineAm, DueAt: NineAm.AddDays(7), PaidAt: NineAm);

    [Fact]
    public void Stripe_Invoice_Valid_Passes()
        => new ProcessStripeEventCommandValidator().TestValidate(Invoice()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Stripe_NegativeCreditTotal_Passes()
        => new ProcessStripeEventCommandValidator().TestValidate(Invoice(total: -15.25m)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Stripe_CheckoutEventWithoutInvoiceFields_Passes()
        => new ProcessStripeEventCommandValidator()
            .TestValidate(new ProcessStripeEventCommand("checkout.session.completed", "sub_1", "cus_1", null, null, null, Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("10000000000.00")]
    [InlineData("-10000000000.00")]
    [InlineData("1.005")]
    public void Stripe_AmountThatDoesNotFitDecimal12_2_Fails(string amount)
        => new ProcessStripeEventCommandValidator()
            .TestValidate(Invoice(total: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)))
            .ShouldHaveValidationErrorFor(x => x.TotalAmount);

    [Theory]
    [InlineData("us")]
    [InlineData("usdx")]
    [InlineData("u$d")]
    public void Stripe_BadCurrency_Fails(string currency)
        => new ProcessStripeEventCommandValidator().TestValidate(Invoice(currency: currency))
            .ShouldHaveValidationErrorFor(x => x.Currency);

    [Fact]
    public void Stripe_PeriodLongerThanThreeYearsOrReversed_Fails()
    {
        var validator = new ProcessStripeEventCommandValidator();
        validator.TestValidate(Invoice(periodEnd: NineAm.AddYears(3).AddDays(10))).ShouldHaveValidationErrorFor("PeriodEnd");
        validator.TestValidate(Invoice(periodEnd: NineAm.AddDays(-1))).ShouldHaveValidationErrorFor("PeriodEnd");
        validator.TestValidate(Invoice(periodEnd: NineAm.AddYears(3))).ShouldNotHaveValidationErrorFor("PeriodEnd");
    }

    [Fact]
    public void Stripe_InvoiceNumberOverColumn_Fails()
        => new ProcessStripeEventCommandValidator().TestValidate(Invoice(invoiceNumber: new string('9', 51)))
            .ShouldHaveValidationErrorFor(x => x.InvoiceNumber);

    [Fact]
    public void Stripe_GatewayRefsOverColumn_Fail()
    {
        var result = new ProcessStripeEventCommandValidator().TestValidate(
            new ProcessStripeEventCommand("customer.subscription.updated", new string('s', 201), new string('c', 201),
                new string('x', 51), null, null, null));
        result.ShouldHaveValidationErrorFor(x => x.StripeSubscriptionId);
        result.ShouldHaveValidationErrorFor(x => x.StripeCustomerId);
        result.ShouldHaveValidationErrorFor(x => x.SubscriptionStatus);
    }
}
