using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.OrgStructure.Positions;
using ONEVO.Api.Controllers.Tenant.OrgStructure;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;
using ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;
using ONEVO.Application.Features.OrgStructure.Queries.ListPositions;
using Xunit;

namespace ONEVO.Tests.Unit.Controllers.Tenant.OrgStructure;

public sealed class PositionsControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly PositionsController _sut;
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();

    public PositionsControllerTests()
    {
        _sut = new PositionsController(_mediatorMock.Object);
    }

    private static PositionResponse SamplePositionResponse(Guid legalEntityId, Guid id, Guid departmentId, string name) => new(
        id, legalEntityId, departmentId, name, "POS-01", "unique", 1, null, true,
        DateTimeOffset.UtcNow, null, "Engineering", null, 0);

    [Fact]
    public async Task List_UsesDefaultQueryValues_WhenNoneProvided()
    {
        var page = new PositionPageResponse([], 1, 25, 0, 0);
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionPageResponse>.Success(page));

        var result = await _sut.List(_legalEntityId, ct: CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListPositionsQuery>(q =>
                q.LegalEntityId == _legalEntityId &&
                q.DepartmentId == null &&
                q.Search == null &&
                q.IncludeInactive == false &&
                q.Page == 1 &&
                q.PageSize == 25 &&
                q.SortBy == "name" &&
                q.SortDirection == "asc"),
            It.IsAny<CancellationToken>()), Times.Once);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(page);
    }

    [Fact]
    public async Task List_ForwardsExplicitQueryValues_ToMediator()
    {
        var page = new PositionPageResponse([], 2, 10, 0, 0);
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionPageResponse>.Success(page));

        var result = await _sut.List(
            _legalEntityId,
            departmentId: _departmentId,
            search: "eng",
            includeInactive: true,
            page: 2,
            pageSize: 10,
            sortBy: "code",
            sortDirection: "desc",
            ct: CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListPositionsQuery>(q =>
                q.LegalEntityId == _legalEntityId &&
                q.DepartmentId == _departmentId &&
                q.Search == "eng" &&
                q.IncludeInactive == true &&
                q.Page == 2 &&
                q.PageSize == 10 &&
                q.SortBy == "code" &&
                q.SortDirection == "desc"),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_SendsQuery_WithRouteIds_AndReturnsOk()
    {
        var sample = SamplePositionResponse(_legalEntityId, _positionId, _departmentId, "Software Engineer");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPositionByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionResponse>.Success(sample));

        var result = await _sut.Get(_legalEntityId, _positionId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<GetPositionByIdQuery>(q => q.LegalEntityId == _legalEntityId && q.PositionId == _positionId),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(sample);
    }

    [Fact]
    public async Task Get_NotFoundResult_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPositionByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionResponse>.NotFound("Position not found."));

        var result = await _sut.Get(_legalEntityId, _positionId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Tree_SendsQuery_WithRouteIdAndIncludeInactive_AndReturnsOk()
    {
        IReadOnlyList<PositionTreeNodeResponse> tree =
        [
            new PositionTreeNodeResponse(_positionId, _legalEntityId, _departmentId, "CEO", "CEO", "unique", 1, null, true, 0, [])
        ];

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPositionTreeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PositionTreeNodeResponse>>.Success(tree));

        var result = await _sut.Tree(_legalEntityId, includeInactive: true, ct: CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<GetPositionTreeQuery>(q => q.LegalEntityId == _legalEntityId && q.IncludeInactive == true),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(tree);
    }

    [Fact]
    public async Task Create_MapsRequestBodyAndRouteLegalEntityId_IntoCommand_AndReturnsCreatedAtAction()
    {
        var created = SamplePositionResponse(_legalEntityId, _positionId, _departmentId, "Software Engineer");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreatePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionResponse>.Success(created));

        var request = new CreatePositionRequest(_departmentId, "Software Engineer", "SWE-1", "unique", 1, null);

        var result = await _sut.Create(_legalEntityId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CreatePositionCommand>(c =>
                c.LegalEntityId == _legalEntityId &&
                c.DepartmentId == _departmentId &&
                c.Name == "Software Engineer" &&
                c.Code == "SWE-1" &&
                c.PositionType == "unique" &&
                c.MaxOccupancy == 1 &&
                c.ReportsToPositionId == null),
            It.IsAny<CancellationToken>()), Times.Once);

        var createdAtResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdAtResult.StatusCode.Should().Be(201);
        createdAtResult.ActionName.Should().Be(nameof(PositionsController.Get));
        createdAtResult.RouteValues!["legalEntityId"].Should().Be(_legalEntityId);
        createdAtResult.RouteValues!["positionId"].Should().Be(created.Id);
        createdAtResult.Value.Should().Be(created);
    }

    [Fact]
    public async Task Create_Conflict_ReturnsProblem409()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreatePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionResponse>.Conflict("Position code already exists in this legal entity."));

        var request = new CreatePositionRequest(_departmentId, "Software Engineer", "SWE-1", "unique", 1, null);

        var result = await _sut.Create(_legalEntityId, request, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_MapsRequestBodyAndRouteIds_IntoCommand_AndReturnsOk()
    {
        var updated = SamplePositionResponse(_legalEntityId, _positionId, _departmentId, "Senior Software Engineer");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdatePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionResponse>.Success(updated));

        var request = new UpdatePositionRequest(_departmentId, "Senior Software Engineer", "SSWE-1", "unique", 1, null);

        var result = await _sut.Update(_legalEntityId, _positionId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<UpdatePositionCommand>(c =>
                c.LegalEntityId == _legalEntityId &&
                c.PositionId == _positionId &&
                c.DepartmentId == _departmentId &&
                c.Name == "Senior Software Engineer" &&
                c.Code == "SSWE-1" &&
                c.PositionType == "unique" &&
                c.MaxOccupancy == 1 &&
                c.ReportsToPositionId == null),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(updated);
    }

    [Fact]
    public async Task Update_Conflict_ReturnsProblem409()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdatePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionResponse>.Conflict("Position cannot report to itself."));

        var request = new UpdatePositionRequest(_departmentId, "Senior Software Engineer", "SSWE-1", "unique", 1, _positionId);

        var result = await _sut.Update(_legalEntityId, _positionId, request, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ArchiveCheck_SendsCommand_WithRouteIds_AndReturnsOk()
    {
        var blockers = new PositionArchiveBlockers(null, false, 0, 0);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CheckPositionArchiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionArchiveBlockers>.Success(blockers));

        var result = await _sut.ArchiveCheck(_legalEntityId, _positionId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CheckPositionArchiveCommand>(c => c.LegalEntityId == _legalEntityId && c.PositionId == _positionId),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(blockers);
    }

    [Fact]
    public async Task ArchiveCheck_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CheckPositionArchiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionArchiveBlockers>.NotFound("Position not found."));

        var result = await _sut.ArchiveCheck(_legalEntityId, _positionId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Archive_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchivePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Archive(_legalEntityId, _positionId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ArchivePositionCommand>(c => c.LegalEntityId == _legalEntityId && c.PositionId == _positionId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Archive_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchivePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Position not found."));

        var result = await _sut.Archive(_legalEntityId, _positionId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Restore_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestorePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Restore(_legalEntityId, _positionId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<RestorePositionCommand>(c => c.LegalEntityId == _legalEntityId && c.PositionId == _positionId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Restore_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestorePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Position not found."));

        var result = await _sut.Restore(_legalEntityId, _positionId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public void Constructor_InjectsIMediatorOnly()
    {
        var constructors = typeof(PositionsController).GetConstructors();
        constructors.Should().HaveCount(1);
        constructors[0].GetParameters().Should().HaveCount(1);
        constructors[0].GetParameters()[0].ParameterType.Should().Be(typeof(IMediator));
    }
}
