using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmploymentType;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeeEmploymentType;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.BulkStartOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class BulkEmployeeCommandsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly FakeUnitOfWork _uow = new();
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public async Task BulkChangePosition_DispatchesOneCommandPerEmployee_AndMapsPendingApproval()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var positionId = Guid.NewGuid(); var managerId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.Is<ChangeEmployeePositionCommand>(x => x.EmployeeId == a), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(false)));
        _mediator.Setup(m => m.Send(It.Is<ChangeEmployeePositionCommand>(x => x.EmployeeId == b), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(true)));
        _mediator.Setup(m => m.Send(It.Is<ChangeEmployeePositionCommand>(x => x.EmployeeId == c), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChangeEmployeePositionResponse>.Forbidden("You do not have access to manage this employee."));

        var handler = new BulkChangeEmployeePositionCommandHandler(_mediator.Object, _uow, NullLogger<BulkChangeEmployeePositionCommandHandler>.Instance);
        var result = await handler.Handle(new BulkChangeEmployeePositionCommand(new[] { a, b, c }, positionId, Today, "Transfer", managerId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "succeeded", "pendingApproval", "failed" }, result.Value!.Items.Select(i => i.Outcome));
        Assert.Equal("You do not have access to manage this employee.", result.Value.Items[2].Reason);
        _mediator.Verify(m => m.Send(It.Is<ChangeEmployeePositionCommand>(x =>
            x.EmployeeId == a && x.PositionId == positionId && x.EffectiveFrom == Today && x.ChangeReason == "Transfer" && x.ReportsToEmployeeId == managerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkChangeEmploymentType_DispatchesNarrowCommand()
    {
        var a = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<ChangeEmployeeEmploymentTypeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var handler = new BulkChangeEmploymentTypeCommandHandler(_mediator.Object, _uow, NullLogger<BulkChangeEmploymentTypeCommandHandler>.Instance);
        var result = await handler.Handle(new BulkChangeEmploymentTypeCommand(new[] { a }, "contract"), CancellationToken.None);

        Assert.Equal(1, result.Value!.Succeeded);
        _mediator.Verify(m => m.Send(It.Is<ChangeEmployeeEmploymentTypeCommand>(x => x.EmployeeId == a && x.EmploymentTypeCode == "contract"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkStartOffboarding_DispatchesStartOffboardingPerEmployee()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.Is<StartOffboardingCommand>(x => x.EmployeeId == a), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));
        _mediator.Setup(m => m.Send(It.Is<StartOffboardingCommand>(x => x.EmployeeId == b), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Conflict("This employee already has an offboarding in progress."));

        var handler = new BulkStartOffboardingCommandHandler(_mediator.Object, _uow, NullLogger<BulkStartOffboardingCommandHandler>.Instance);
        var result = await handler.Handle(
            new BulkStartOffboardingCommand(new[] { a, b }, "resignation", Today, "medium", null, "note"), CancellationToken.None);

        Assert.Equal((1, 0, 1), (result.Value!.Succeeded, result.Value.PendingApproval, result.Value.Failed));
        _mediator.Verify(m => m.Send(It.Is<StartOffboardingCommand>(x =>
            x.EmployeeId == a && x.Reason == "resignation" && x.LastWorkingDate == Today && x.KnowledgeRiskLevel == "medium" && x.Notes == "note"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Validators_RejectEmptyAndOverLimit_AcceptDuplicatesWithinLimit()
    {
        var validator = new BulkChangeEmploymentTypeCommandValidator();
        Assert.False(validator.Validate(new BulkChangeEmploymentTypeCommand(Array.Empty<Guid>(), "contract")).IsValid);
        Assert.False(validator.Validate(new BulkChangeEmploymentTypeCommand(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList(), "contract")).IsValid);
        var id = Guid.NewGuid();
        Assert.True(validator.Validate(new BulkChangeEmploymentTypeCommand(Enumerable.Repeat(id, 150).ToList(), "contract")).IsValid);

        Assert.False(new BulkChangeEmployeePositionCommandValidator().Validate(
            new BulkChangeEmployeePositionCommand(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList(), Guid.NewGuid(), Today, "Promotion", null)).IsValid);
        Assert.False(new BulkStartOffboardingCommandValidator().Validate(
            new BulkStartOffboardingCommand(Array.Empty<Guid>(), "resignation", Today, "medium", null, null)).IsValid);
    }
}
