using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class CreateProjectPictureValidationTests
{
    private static CreateProjectCommand Command(string? icon, Stream? logo) => new(
        Guid.NewGuid(), "Website Revamp", "WEB", "desc",
        new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), null,
        "", 0m, 40m, [], logo is null ? null : "logo.png", logo is null ? null : "image/png", logo, Icon: icon);

    [Fact]
    public void IconAndLogoTogether_AreRejected()
    {
        using var logo = new MemoryStream([1]);

        var result = new CreateProjectCommandValidator().Validate(Command("lucide:rocket", logo));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Choose an icon or a logo, not both.");
    }

    [Fact]
    public void IconAlone_IsAccepted()
    {
        var result = new CreateProjectCommandValidator().Validate(Command("lucide:rocket", null));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateProjectCommand.Icon));
    }

    [Fact]
    public void LogoAlone_IsAccepted()
    {
        using var logo = new MemoryStream([1]);

        var result = new CreateProjectCommandValidator().Validate(Command(null, logo));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateProjectCommand.Icon));
    }
}
