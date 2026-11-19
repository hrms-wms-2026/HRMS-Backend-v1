using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public class SaveOnboardingDraftCommandValidatorTests
{
    private static SaveOnboardingDraftCommand Partial(string? email = null, string? first = null) => new(
        DraftId: null, FirstName: first, LastName: null, WorkEmail: email, LegalEntityId: Guid.NewGuid(),
        DepartmentId: null, PositionId: null, EmploymentType: null, StartDate: null, EmployeeNumber: null,
        WorkModeId: null, SelectedTemplateId: null, EditedTasksJson: null, LastSavedStep: "employee_details",
        IfMatchVersion: null, ReportsToEmployeeId: null);

    [Fact]
    public void OnlyCompanyAndStep_IsValid() =>
        Assert.True(new SaveOnboardingDraftCommandValidator().Validate(Partial()).IsValid);

    [Fact]
    public void MissingCompany_IsInvalid() =>
        Assert.False(new SaveOnboardingDraftCommandValidator().Validate(Partial() with { LegalEntityId = Guid.Empty }).IsValid);

    [Fact]
    public void MalformedEmail_WhenProvided_IsInvalid() =>
        Assert.False(new SaveOnboardingDraftCommandValidator().Validate(Partial(email: "not-an-email")).IsValid);

    [Fact]
    public void OverlongName_WhenProvided_IsInvalid() =>
        Assert.False(new SaveOnboardingDraftCommandValidator().Validate(Partial(first: new string('a', 101))).IsValid);
}
