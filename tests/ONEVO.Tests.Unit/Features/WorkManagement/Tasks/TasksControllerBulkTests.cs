using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.BulkTaskActions;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TasksControllerBulkTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly TasksController _sut;
    private readonly BulkTaskActionResponse _response =
        BulkTaskActionResponse.From(Array.Empty<BulkItemResult>());

    public TasksControllerBulkTests() => _sut = new TasksController(_mediator.Object);

    [Fact]
    public async Task BulkStatus_SendsCommand_AndReturnsOk()
    {
        var ids = new[] { Guid.NewGuid() };
        var statusId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<BulkMoveTaskStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkTaskActionResponse>.Success(_response));

        var result = await _sut.BulkMoveStatus(
            new BulkMoveTaskStatusRequest(ids, statusId), CancellationToken.None);

        Assert.Same(_response, Assert.IsType<OkObjectResult>(result).Value);
        _mediator.Verify(m => m.Send(It.Is<BulkMoveTaskStatusCommand>(c =>
            c.TaskIds.SequenceEqual(ids) && c.NewStatusId == statusId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkAssign_SendsCommand_AndReturnsOk()
    {
        var ids = new[] { Guid.NewGuid() };
        var employeeId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<BulkAssignTaskCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkTaskActionResponse>.Success(_response));

        var result = await _sut.BulkAssign(
            new BulkAssignTaskRequest(ids, employeeId), CancellationToken.None);

        Assert.Same(_response, Assert.IsType<OkObjectResult>(result).Value);
        _mediator.Verify(m => m.Send(It.Is<BulkAssignTaskCommand>(c =>
            c.TaskIds.SequenceEqual(ids) && c.EmployeeId == employeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkPriority_SendsCommand_AndReturnsOk()
    {
        var ids = new[] { Guid.NewGuid() };
        _mediator.Setup(m => m.Send(It.IsAny<BulkSetTaskPriorityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkTaskActionResponse>.Success(_response));

        var result = await _sut.BulkSetPriority(
            new BulkSetTaskPriorityRequest(ids, "high"), CancellationToken.None);

        Assert.Same(_response, Assert.IsType<OkObjectResult>(result).Value);
        _mediator.Verify(m => m.Send(It.Is<BulkSetTaskPriorityCommand>(c =>
            c.TaskIds.SequenceEqual(ids) && c.Priority == "high"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkDueDate_PassesNullThrough_AndReturnsOk()
    {
        var ids = new[] { Guid.NewGuid() };
        _mediator.Setup(m => m.Send(It.IsAny<BulkSetTaskDueDateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkTaskActionResponse>.Success(_response));

        var result = await _sut.BulkSetDueDate(
            new BulkSetTaskDueDateRequest(ids, null), CancellationToken.None);

        Assert.Same(_response, Assert.IsType<OkObjectResult>(result).Value);
        _mediator.Verify(m => m.Send(It.Is<BulkSetTaskDueDateCommand>(c =>
            c.TaskIds.SequenceEqual(ids) && c.DueDate == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkConvertToSubtask_SendsCommand_AndReturnsOk()
    {
        var ids = new[] { Guid.NewGuid() };
        var parentId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<BulkConvertTasksToSubtasksCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkTaskActionResponse>.Success(_response));

        var result = await _sut.BulkConvertToSubtask(
            new BulkConvertTasksToSubtasksRequest(ids, parentId), CancellationToken.None);

        Assert.Same(_response, Assert.IsType<OkObjectResult>(result).Value);
        _mediator.Verify(m => m.Send(It.Is<BulkConvertTasksToSubtasksCommand>(c =>
            c.TaskIds.SequenceEqual(ids) && c.NewParentTaskId == parentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_SendsCommand_AndReturnsOk()
    {
        var ids = new[] { Guid.NewGuid() };
        _mediator.Setup(m => m.Send(It.IsAny<BulkDeleteTasksCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkTaskActionResponse>.Success(_response));

        var result = await _sut.BulkDelete(new BulkDeleteTasksRequest(ids), CancellationToken.None);

        Assert.Same(_response, Assert.IsType<OkObjectResult>(result).Value);
        _mediator.Verify(m => m.Send(It.Is<BulkDeleteTasksCommand>(c =>
            c.TaskIds.SequenceEqual(ids)), It.IsAny<CancellationToken>()), Times.Once);
    }
}
