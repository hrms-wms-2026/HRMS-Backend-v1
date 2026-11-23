using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Tests.Unit.Features.Validation;

public sealed class ValidationRuleExtensionsTests
{
    private sealed record Sample(
        string? Name = "ok", string? Optional = null, string? Html = null, IReadOnlyList<Guid>? Ids = null,
        DateTimeOffset? From = null, DateTimeOffset? To = null, DateOnly? FromDay = null, DateOnly? ToDay = null,
        int Page = 1, int Size = 20);

    private sealed class SampleValidator : AbstractValidator<Sample>
    {
        public SampleValidator()
        {
            RuleFor(x => x.Name).ValidName(10);
            RuleFor(x => x.Optional).ValidName(5, required: false);
            RuleFor(x => x.Html).ValidRichText(50);
            RuleFor(x => x.Ids).ValidIdList(3);
            RuleFor(x => x).ValidDateRange(x => x.From, x => x.To, TimeSpan.FromHours(24));
            RuleFor(x => x).ValidDateRange(x => x.FromDay, x => x.ToDay, 31);
            this.ValidPageRequest(x => x.Page, x => x.Size, 100);
        }
    }

    private readonly SampleValidator _validator = new();

    [Fact]
    public void Defaults_Pass()
        => _validator.TestValidate(new Sample()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidName_Required_RejectsMissingOrBlank(string? name)
        => _validator.TestValidate(new Sample(Name: name)).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void ValidName_AtMaxLength_Passes()
        => _validator.TestValidate(new Sample(Name: new string('a', 10))).ShouldNotHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void ValidName_OverMaxLength_Fails()
        => _validator.TestValidate(new Sample(Name: new string('a', 11))).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void ValidName_Optional_AllowsNullAndEmpty_ButNotTooLong()
    {
        _validator.TestValidate(new Sample(Optional: null)).ShouldNotHaveValidationErrorFor(x => x.Optional);
        _validator.TestValidate(new Sample(Optional: "")).ShouldNotHaveValidationErrorFor(x => x.Optional);
        _validator.TestValidate(new Sample(Optional: "123456")).ShouldHaveValidationErrorFor(x => x.Optional);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    public void ValidRichText_RejectsActiveContent(string html)
        => _validator.TestValidate(new Sample(Html: html)).ShouldHaveValidationErrorFor(x => x.Html)
            .WithErrorMessage(RichTextRules.ActiveContentError);

    [Fact]
    public void ValidRichText_AllowsFormattingAndEmpty()
    {
        _validator.TestValidate(new Sample(Html: "<p><b>Hi</b> there</p>")).ShouldNotHaveValidationErrorFor(x => x.Html);
        _validator.TestValidate(new Sample(Html: "")).ShouldNotHaveValidationErrorFor(x => x.Html);
    }

    [Fact]
    public void ValidRichText_OverMaxLength_Fails()
        => _validator.TestValidate(new Sample(Html: new string('a', 51))).ShouldHaveValidationErrorFor(x => x.Html);

    [Fact]
    public void ValidIdList_AllowsNullAndUpToMax_RejectsMore()
    {
        _validator.TestValidate(new Sample(Ids: null)).ShouldNotHaveValidationErrorFor(x => x.Ids);
        _validator.TestValidate(new Sample(Ids: Ids(3))).ShouldNotHaveValidationErrorFor(x => x.Ids);
        _validator.TestValidate(new Sample(Ids: Ids(4))).ShouldHaveValidationErrorFor(x => x.Ids);
    }

    [Fact]
    public void ValidDateRange_EndBeforeStart_FailsOnEnd()
    {
        var start = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        _validator.TestValidate(new Sample(From: start, To: start.AddMinutes(-1))).ShouldHaveValidationErrorFor("To");
    }

    [Fact]
    public void ValidDateRange_LongerThanMaxSpan_Fails_ExactlyMaxSpan_Passes()
    {
        var start = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        _validator.TestValidate(new Sample(From: start, To: start.AddHours(24))).ShouldNotHaveValidationErrorFor("To");
        _validator.TestValidate(new Sample(From: start, To: start.AddHours(24).AddSeconds(1))).ShouldHaveValidationErrorFor("To");
    }

    [Fact]
    public void ValidDateRange_OneEndMissing_IsLeftToTheHandler()
        => _validator.TestValidate(new Sample(From: DateTimeOffset.UtcNow, To: null)).ShouldNotHaveValidationErrorFor("To");

    [Fact]
    public void ValidDateRange_DateOnly_EnforcesOrderAndMaxDays()
    {
        var day = new DateOnly(2026, 1, 1);
        _validator.TestValidate(new Sample(FromDay: day, ToDay: day.AddDays(31))).ShouldNotHaveValidationErrorFor("ToDay");
        _validator.TestValidate(new Sample(FromDay: day, ToDay: day.AddDays(32))).ShouldHaveValidationErrorFor("ToDay");
        _validator.TestValidate(new Sample(FromDay: day, ToDay: day.AddDays(-1))).ShouldHaveValidationErrorFor("ToDay");
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void ValidPageRequest_RejectsOutOfRange(int page, int size)
        => _validator.TestValidate(new Sample(Page: page, Size: size)).Errors.Should().NotBeEmpty();

    [Fact]
    public void ValidPageRequest_AllowsMaxPageSize()
        => _validator.TestValidate(new Sample(Page: 3, Size: 100)).ShouldNotHaveAnyValidationErrors();

    internal static IReadOnlyList<Guid> Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
}
