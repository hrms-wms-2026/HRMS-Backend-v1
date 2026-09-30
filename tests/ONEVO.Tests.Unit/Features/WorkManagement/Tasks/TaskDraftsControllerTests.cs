using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.TaskDrafts;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.TaskDrafts;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class TaskDraftsControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly TaskDraftsController _sut;
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    public TaskDraftsControllerTests() => _sut = new TaskDraftsController(_mediator.Object);

    private static SaveTaskDraftRequest Request() =>
        new(ProjectId, "t", JsonDocument.Parse("{\"priority\":\"high\"}").RootElement);

    private static TaskDraftResponse Response() => new(Id, ProjectId, "t", "{}", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_AndSendsRawPayload()
    {
        _mediator.Setup(m => m.Send(It.IsAny<SaveTaskDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskDraftResponse>.Success(Response()));

        var result = await _sut.Create(Request(), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
        _mediator.Verify(m => m.Send(
            It.Is<SaveTaskDraftCommand>(c => c.DraftId == null && c.PayloadJson == "{\"priority\":\"high\"}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_PassesRouteIdAsDraftId()
    {
        _mediator.Setup(m => m.Send(It.IsAny<SaveTaskDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskDraftResponse>.Success(Response()));

        var result = await _sut.Update(Id, Request(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _mediator.Verify(m => m.Send(It.Is<SaveTaskDraftCommand>(c => c.DraftId == Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        _mediator.Setup(m => m.Send(It.IsAny<DeleteTaskDraftCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        Assert.IsType<NoContentResult>(await _sut.Delete(Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_ReturnsOk()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ListMyTaskDraftsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskDraftSummaryResponse>>.Success([]));
        Assert.IsType<OkObjectResult>(await _sut.List(CancellationToken.None));
    }

    [Fact]
    public async Task Get_ReturnsOk_WhenFound_And404WhenMissing()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetTaskDraftQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskDraftResponse>.Success(Response()));
        Assert.IsType<OkObjectResult>(await _sut.Get(Id, CancellationToken.None));

        _mediator.Setup(m => m.Send(It.IsAny<GetTaskDraftQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskDraftResponse>.NotFound("Draft not found."));
        var missing = Assert.IsAssignableFrom<ObjectResult>(await _sut.Get(Id, CancellationToken.None));
        Assert.Equal(404, missing.StatusCode);
    }
}
