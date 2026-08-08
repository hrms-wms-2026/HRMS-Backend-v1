using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.OrgStructure.LegalEntities;
using ONEVO.Api.Controllers.Tenant.OrgStructure;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public sealed class LegalEntitiesControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly LegalEntitiesController _sut;

    public LegalEntitiesControllerTests()
    {
        _sut = new LegalEntitiesController(_mediator.Object);
    }

    private static LegalEntityGeneralSettingsResponse SampleGeneralSettings(Guid id) => new(
        id, "Acme Lanka", "ACME", null, "REG-001", null, null, null, null, null,
        "LKA", "LKR", "UTC", 1, 1, [1, 2, 3, 4, 5], "en-US", "DD MMM YYYY", "12h", "active");

    [Fact]
    public async Task List_SendsQuery_WithIncludeInactiveValue()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ListLegalEntitiesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LegalEntityListItemResponse>>.Success([]));

        await _sut.List(includeInactive: true, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<ListLegalEntitiesQuery>(q => q.IncludeInactive == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_Failure_ReturnsProblemWithStatusCode()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ListLegalEntitiesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LegalEntityListItemResponse>>.Forbidden("Authentication required."));

        var result = await _sut.List(includeInactive: false, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetGeneralSettings_SendsQuery_WithRouteId()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<GetLegalEntityGeneralSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LegalEntityGeneralSettingsResponse>.Success(SampleGeneralSettings(id)));

        var result = await _sut.GetGeneralSettings(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<GetLegalEntityGeneralSettingsQuery>(q => q.LegalEntityId == id),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetGeneralSettings_NotFound_ReturnsProblem404()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetLegalEntityGeneralSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LegalEntityGeneralSettingsResponse>.NotFound("Company not found."));

        var result = await _sut.GetGeneralSettings(Guid.NewGuid(), CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_SendsCommand_AndReturnsCreatedAtGetGeneralSettings()
    {
        var created = SampleGeneralSettings(Guid.NewGuid());
        _mediator.Setup(m => m.Send(It.IsAny<CreateLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LegalEntityGeneralSettingsResponse>.Success(created));

        var request = new CreateLegalEntityRequest(
            "Acme Lanka", "ACME", "REG-001", "LKA", "LKR", null, null);

        var result = await _sut.Create(request, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<CreateLegalEntityCommand>(c => c.Name == "Acme Lanka" && c.CompanyCode == "ACME"),
            It.IsAny<CancellationToken>()), Times.Once);

        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(LegalEntitiesController.GetGeneralSettings));
        createdResult.RouteValues!["id"].Should().Be(created.Id);
        createdResult.Value.Should().Be(created);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsProblem409()
    {
        _mediator.Setup(m => m.Send(It.IsAny<CreateLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LegalEntityGeneralSettingsResponse>.Conflict("Company name already exists."));

        var request = new CreateLegalEntityRequest(
            "Acme Lanka", "ACME", "REG-001", "LKA", "LKR", null, null);

        var result = await _sut.Create(request, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UpdateGeneralSettings_UsesRouteId_NotAnyBodyId()
    {
        var routeId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<UpdateLegalEntityGeneralSettingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LegalEntityGeneralSettingsResponse>.Success(SampleGeneralSettings(routeId)));

        var request = new UpdateLegalEntityGeneralSettingsRequest(
            "Acme Lanka", "ACME", "REG-001", null, null, null, null, null,
            "LKA", "LKR", "UTC", 1, 1, [1, 2, 3, 4, 5], "en-US", "DD MMM YYYY", "12h", "active");

        var result = await _sut.UpdateGeneralSettings(routeId, request, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<UpdateLegalEntityGeneralSettingsCommand>(c => c.LegalEntityId == routeId),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Delete_SendsCommand_AndReturnsNoContent()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<DeleteLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _sut.Delete(id, new DeleteLegalEntityRequest("Acme Lanka"), CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<DeleteLegalEntityCommand>(c => c.LegalEntityId == id && c.ConfirmName == "Acme Lanka"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_ConfirmNameMismatch_ReturnsProblem400()
    {
        _mediator.Setup(m => m.Send(It.IsAny<DeleteLegalEntityCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Company name confirmation does not match."));

        var result = await _sut.Delete(Guid.NewGuid(), new DeleteLegalEntityRequest("Wrong"), CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task RemoveLogo_SendsCommand_AndReturnsNoContent()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<RemoveLegalEntityLogoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _sut.RemoveLogo(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<RemoveLegalEntityLogoCommand>(c => c.LegalEntityId == id),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<NoContentResult>();
    }
}
