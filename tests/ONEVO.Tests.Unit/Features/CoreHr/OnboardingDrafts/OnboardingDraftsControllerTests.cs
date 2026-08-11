using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.CoreHr.OnboardingDrafts;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.FinalizeOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Queries.GetOnboardingDraft;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class OnboardingDraftsControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly OnboardingDraftsController _sut;

    public OnboardingDraftsControllerTests()
    {
        _sut = new OnboardingDraftsController(_mediator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static SaveOnboardingDraftRequest SampleRequest() => new(
        "Ada", "Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
        "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, 1, null, null, "employee_details");

    private static OnboardingDraftResponse SampleResponse(Guid id) => new(
        id, "Ada", "Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
        "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, 1, null, null,
        "waiting_for_seat", "waiting_for_seat", "employee_details", Guid.NewGuid(), "1");

    [Fact]
    public async Task Create_SendsCommandWithNullDraftIdAndNullIfMatch()
    {
        var response = SampleResponse(Guid.NewGuid());
        _mediator
            .Setup(m => m.Send(It.IsAny<SaveOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingDraftResponse>.Success(response));

        await _sut.Create(SampleRequest(), CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<SaveOnboardingDraftCommand>(c => c.DraftId == null && c.IfMatchVersion == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Returns201_WithLocationPointingAtGetById()
    {
        var response = SampleResponse(Guid.NewGuid());
        _mediator
            .Setup(m => m.Send(It.IsAny<SaveOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingDraftResponse>.Success(response));

        var result = await _sut.Create(SampleRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(OnboardingDraftsController.GetById), created.ActionName);
        Assert.Same(response, created.Value);
    }

    [Fact]
    public async Task Create_ReturnsConflict_OnDuplicateEmail()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SaveOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingDraftResponse>.Conflict("duplicate"));

        var result = await _sut.Create(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(409, objectResult.StatusCode);
    }

    [Fact]
    public async Task Update_SendsCommandWithRouteDraftId()
    {
        var draftId = Guid.NewGuid();
        var response = SampleResponse(draftId);
        _mediator
            .Setup(m => m.Send(It.IsAny<SaveOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingDraftResponse>.Success(response));

        await _sut.Update(draftId, SampleRequest(), CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<SaveOnboardingDraftCommand>(c => c.DraftId == draftId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsConflict_WhenHandlerReportsConcurrencyConflict()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SaveOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingDraftResponse>.Conflict("stale"));

        var result = await _sut.Update(Guid.NewGuid(), SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(409, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns404_WhenNotFound()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetOnboardingDraftQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingDraftResponse>.NotFound("missing"));

        var result = await _sut.GetById(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
    }

    [Fact]
    public async Task Finalize_SendsCommandWithRouteDraftId()
    {
        var draftId = Guid.NewGuid();
        var response = new FinalizeOnboardingDraftResponse(
            draftId, Guid.NewGuid(), "finalized", "invitation_sent", true, false, false, "onboarding.finalize.invitation_sent");
        _mediator
            .Setup(m => m.Send(It.IsAny<FinalizeOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FinalizeOnboardingDraftResponse>.Success(response));

        await _sut.Finalize(draftId, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<FinalizeOnboardingDraftCommand>(c => c.DraftId == draftId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Finalize_Returns200_OnSuccess()
    {
        var draftId = Guid.NewGuid();
        var response = new FinalizeOnboardingDraftResponse(
            draftId, Guid.NewGuid(), "finalized", "invitation_sent", true, false, false, "onboarding.finalize.invitation_sent");
        _mediator
            .Setup(m => m.Send(It.IsAny<FinalizeOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FinalizeOnboardingDraftResponse>.Success(response));

        var result = await _sut.Finalize(draftId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, okResult.Value);
    }

    [Fact]
    public async Task Finalize_MapsFailureToProblem_WithHandlerStatusCode()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<FinalizeOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FinalizeOnboardingDraftResponse>.Conflict("This draft has already been finalized."));

        var result = await _sut.Finalize(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(409, objectResult.StatusCode);
    }

    [Fact]
    public async Task Finalize_Returns404_WhenDraftMissing()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<FinalizeOnboardingDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FinalizeOnboardingDraftResponse>.NotFound("missing"));

        var result = await _sut.Finalize(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
    }
}
