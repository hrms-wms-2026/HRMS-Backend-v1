using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingWorkModes.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingWorkModes.Queries.ListOnboardingWorkModes;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingWorkModes;

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
        var legalEntityId = Guid.NewGuid();
        var workModes = new List<OnboardingWorkModeResponse>
        {
            new(Guid.NewGuid(), "Remote"),
            new(Guid.NewGuid(), "Hybrid")
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<ListOnboardingWorkModesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<OnboardingWorkModeResponse>>.Success(workModes));

        var result = await _sut.List(legalEntityId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(workModes, okResult.Value);
    }

    [Fact]
    public async Task List_SendsListOnboardingWorkModesQuery_WithLegalEntityIdFromQueryString()
    {
        var legalEntityId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.IsAny<ListOnboardingWorkModesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<OnboardingWorkModeResponse>>.Success([]));

        await _sut.List(legalEntityId, CancellationToken.None);

        _mediator.Verify(
            m => m.Send(
                It.Is<ListOnboardingWorkModesQuery>(q => q.LegalEntityId == legalEntityId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task List_MapsFailureToProblem_WithHandlerStatusCode()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListOnboardingWorkModesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<OnboardingWorkModeResponse>>.Failure("unexpected", 400));

        var result = await _sut.List(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }
}
