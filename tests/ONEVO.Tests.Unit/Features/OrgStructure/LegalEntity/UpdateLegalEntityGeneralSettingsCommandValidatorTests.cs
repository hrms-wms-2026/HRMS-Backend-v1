using FluentValidation.TestHelper;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class UpdateLegalEntityGeneralSettingsCommandValidatorTests
{
    private readonly UpdateLegalEntityGeneralSettingsCommandValidator _validator = new();

    private static UpdateLegalEntityGeneralSettingsCommand ValidCommand() => new(
        Guid.NewGuid(),
        "Acme Lanka",
        "ACME",
        "REG-001",
        null, null, null, null, null,
        "LKA",
        "LKR",
        "Asia/Colombo",
        1,
        1,
        [1, 2, 3, 4, 5],
        "en-US",
        "DD MMM YYYY",
        "12h",
        "active",
        null,
        null,
        null);

    [Fact]
    public void Valid_Command_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_StandardWorkingDays_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { StandardWorkingDays = [] });
        result.ShouldHaveValidationErrorFor(x => x.StandardWorkingDays);
    }

    [Fact]
    public void OutOfRange_StandardWorkingDays_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { StandardWorkingDays = [0, 8] });
        result.ShouldHaveValidationErrorFor(x => x.StandardWorkingDays);
    }

    [Fact]
    public void Duplicate_StandardWorkingDays_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { StandardWorkingDays = [1, 1, 2] });
        result.ShouldHaveValidationErrorFor(x => x.StandardWorkingDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void OutOfRange_FinancialYearStartMonth_HasError(int month)
    {
        var result = _validator.TestValidate(ValidCommand() with { FinancialYearStartMonth = month });
        result.ShouldHaveValidationErrorFor(x => x.FinancialYearStartMonth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void OutOfRange_FirstDayOfWeek_HasError(int day)
    {
        var result = _validator.TestValidate(ValidCommand() with { FirstDayOfWeek = day });
        result.ShouldHaveValidationErrorFor(x => x.FirstDayOfWeek);
    }

    [Fact]
    public void InvalidTimeFormat_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { TimeFormat = "hh:mm" });
        result.ShouldHaveValidationErrorFor(x => x.TimeFormat);
    }

    [Fact]
    public void InvalidStatus_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Status = "archived" });
        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void InvalidEmail_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Email = "not-an-email" });
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void ValidEmail_HasNoError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Email = "hr@acme.lk" });
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void InvalidWebsite_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Website = "not a url" });
        result.ShouldHaveValidationErrorFor(x => x.Website);
    }

    [Fact]
    public void ValidWebsite_HasNoError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Website = "https://acme.lk" });
        result.ShouldNotHaveValidationErrorFor(x => x.Website);
    }

    [Fact]
    public void Empty_Timezone_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Timezone = "" });
        result.ShouldHaveValidationErrorFor(x => x.Timezone);
    }

    [Fact]
    public void BothWorkTimesNull_HasNoError()
    {
        var result = _validator.TestValidate(ValidCommand() with { WorkStartTime = null, WorkEndTime = null });
        result.ShouldNotHaveValidationErrorFor(x => x.WorkStartTime);
        result.ShouldNotHaveValidationErrorFor(x => x.WorkEndTime);
    }

    [Fact]
    public void ValidWorkStartAndEndTime_HasNoError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            WorkStartTime = new TimeOnly(9, 0),
            WorkEndTime = new TimeOnly(17, 30)
        });
        result.ShouldNotHaveValidationErrorFor(x => x.WorkStartTime);
        result.ShouldNotHaveValidationErrorFor(x => x.WorkEndTime);
    }

    [Fact]
    public void OnlyWorkStartTimeProvided_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            WorkStartTime = new TimeOnly(9, 0),
            WorkEndTime = null
        });
        result.ShouldHaveValidationErrorFor(x => x.WorkEndTime);
    }

    [Fact]
    public void OnlyWorkEndTimeProvided_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            WorkStartTime = null,
            WorkEndTime = new TimeOnly(17, 30)
        });
        result.ShouldHaveValidationErrorFor(x => x.WorkStartTime);
    }

    [Fact]
    public void WorkStartTime_EqualToEndTime_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            WorkStartTime = new TimeOnly(9, 0),
            WorkEndTime = new TimeOnly(9, 0)
        });
        result.ShouldHaveValidationErrorFor(x => x.WorkStartTime);
    }

    [Fact]
    public void WorkStartTime_AfterEndTime_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            WorkStartTime = new TimeOnly(18, 0),
            WorkEndTime = new TimeOnly(9, 0)
        });
        result.ShouldHaveValidationErrorFor(x => x.WorkStartTime);
    }

    [Fact]
    public void NullBreakDurationMinutes_HasNoError()
    {
        var result = _validator.TestValidate(ValidCommand() with { BreakDurationMinutes = null });
        result.ShouldNotHaveValidationErrorFor(x => x.BreakDurationMinutes);
    }

    [Fact]
    public void ZeroBreakDurationMinutes_HasNoError()
    {
        var result = _validator.TestValidate(ValidCommand() with { BreakDurationMinutes = 0 });
        result.ShouldNotHaveValidationErrorFor(x => x.BreakDurationMinutes);
    }

    [Fact]
    public void PositiveBreakDurationMinutes_HasNoError()
    {
        var result = _validator.TestValidate(ValidCommand() with { BreakDurationMinutes = 60 });
        result.ShouldNotHaveValidationErrorFor(x => x.BreakDurationMinutes);
    }

    [Fact]
    public void NegativeBreakDurationMinutes_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { BreakDurationMinutes = -1 });
        result.ShouldHaveValidationErrorFor(x => x.BreakDurationMinutes);
    }

    [Fact]
    public void BreakDurationMinutes_IsIndependentOfWorkTimes()
    {
        var result = _validator.TestValidate(ValidCommand() with
        {
            WorkStartTime = null,
            WorkEndTime = null,
            BreakDurationMinutes = 30
        });
        result.ShouldNotHaveValidationErrorFor(x => x.BreakDurationMinutes);
        result.ShouldNotHaveValidationErrorFor(x => x.WorkStartTime);
        result.ShouldNotHaveValidationErrorFor(x => x.WorkEndTime);
    }
}
