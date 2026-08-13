using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.WorkModes.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.WorkModes.Queries.ListActiveWorkModes;

namespace ONEVO.Tests.Unit.Features.CoreHr.WorkMode;

public sealed class WorkModesControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly WorkModesController _sut;

    public WorkModesControllerTests()
    {
        _sut = new WorkModesController(_mediator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task List_Returns200_WithWorkModes_OnSuccess()
    {
        var workModes = new List<WorkModeDto> { new(1, "on_site", "On-Site"), new(2, "remote", "Remote") };
        _mediator
            .Setup(m => m.Send(It.IsAny<ListActiveWorkModesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<WorkModeDto>>.Success(workModes));

        var result = await _sut.List(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(workModes, okResult.Value);
    }

    [Fact]
    public async Task List_SendsListActiveWorkModesQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListActiveWorkModesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<WorkModeDto>>.Success([]));

        await _sut.List(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ListActiveWorkModesQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_MapsFailureToProblem_WithHandlerStatusCode()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListActiveWorkModesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<WorkModeDto>>.Failure("unexpected", 400));

        var result = await _sut.List(CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }
}
