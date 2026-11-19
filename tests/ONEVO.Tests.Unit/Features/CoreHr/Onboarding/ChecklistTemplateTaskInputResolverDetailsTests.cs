using FluentAssertions;
using Moq;
using ONEVO.Application.Features.CoreHr.Onboarding.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class ChecklistTemplateTaskInputResolverDetailsTests
{
    private readonly ChecklistTemplateTaskInputResolver _resolver = new(new Mock<IPositionRepository>().Object);

    [Fact]
    public async Task ResolveAsync_KeepsTaskDetails()
    {
        var fileId = Guid.NewGuid();
        var input = new ChecklistTemplateTaskInput(
            "Collect laptop", ChecklistTaskOwnerTypes.Employee, null, null, 0, 1, true,
            IsBypassable: true, BypassPenaltyDescription: " Salary hold ", Category: "asset_return",
            Description: "<p>Desc</p>", Priority: "critical", AttachmentFileIds: [fileId, fileId]);

        var result = await _resolver.ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), [input], CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var task = result.Value!.Single();
        task.Description.Should().Be("<p>Desc</p>");
        task.Priority.Should().Be("critical");
        task.AttachmentFileIds.Should().Equal(fileId);
        task.IsBypassable.Should().BeTrue();
        task.BypassPenaltyDescription.Should().Be("Salary hold");
        task.Category.Should().Be("asset_return");
    }

    [Fact]
    public async Task ResolveAsync_DropsPenaltyWhenNotBypassable()
    {
        var input = new ChecklistTemplateTaskInput(
            "Collect laptop", ChecklistTaskOwnerTypes.Employee, null, null, 0, 1, true,
            IsBypassable: false, BypassPenaltyDescription: "Salary hold");

        var result = await _resolver.ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), [input], CancellationToken.None);

        result.Value!.Single().BypassPenaltyDescription.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RejectsUnknownPriority()
    {
        var input = new ChecklistTemplateTaskInput(
            "Collect laptop", ChecklistTaskOwnerTypes.Employee, null, null, 0, 1, true, Priority: "urgent");

        var result = await _resolver.ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), [input], CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
    }
}
