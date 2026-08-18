using System.Reflection;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.OrgStructure.Departments;
using ONEVO.Api.Controllers.Tenant.OrgStructure;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
using ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Department;

public sealed class DepartmentsControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly DepartmentsController _sut;
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();

    public DepartmentsControllerTests()
    {
        _sut = new DepartmentsController(_mediatorMock.Object);
    }

    private static DepartmentResponse SampleDepartmentResponse(Guid legalEntityId, Guid id, string name) => new(
        id, legalEntityId, name, "DEP-01", null, null, true, DateTimeOffset.UtcNow, null);

    private static DepartmentListItemResponse SampleListItemResponse(Guid legalEntityId, Guid id, string name) => new(
        id, legalEntityId, name, "DEP-01", null, null, true, DateTimeOffset.UtcNow, null, 0, 0, null);

    [Fact]
    public async Task List_UsesDefaultQueryValues_WhenNoneProvided()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Success(
                new DepartmentListResult(new DepartmentListPageResponse([], 1, 25, 0, 0), null)));

        var result = await _sut.List(_legalEntityId, ct: CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListDepartmentsQuery>(q =>
                q.LegalEntityId == _legalEntityId &&
                q.Search == null &&
                q.IncludeInactive == false &&
                q.ParentDepartmentId == null &&
                q.View == "flat" &&
                q.SortBy == "name" &&
                q.SortDirection == "asc" &&
                q.Page == 1 &&
                q.PageSize == 25),
            It.IsAny<CancellationToken>()), Times.Once);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DepartmentListPageResponse>();
    }

    [Fact]
    public async Task List_ForwardsExplicitQueryValues_ToMediator()
    {
        var parentId = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Success(
                new DepartmentListResult(new DepartmentListPageResponse([], 2, 10, 0, 0), null)));

        var result = await _sut.List(
            _legalEntityId,
            search: "eng",
            includeInactive: true,
            parentDepartmentId: parentId,
            view: "flat",
            sortBy: "code",
            sortDirection: "desc",
            page: 2,
            pageSize: 10,
            ct: CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListDepartmentsQuery>(q =>
                q.LegalEntityId == _legalEntityId &&
                q.Search == "eng" &&
                q.IncludeInactive == true &&
                q.ParentDepartmentId == parentId &&
                q.View == "flat" &&
                q.SortBy == "code" &&
                q.SortDirection == "desc" &&
                q.Page == 2 &&
                q.PageSize == 10),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task List_TreeView_ReturnsTreePayload()
    {
        var treeResponse = new DepartmentTreeResponse(
        [
            new DepartmentTreeNodeResponse(_departmentId, _legalEntityId, "Engineering", "ENG", null, null, true, [], 0, 0, null)
        ]);
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Success(new DepartmentListResult(null, treeResponse)));

        var result = await _sut.List(_legalEntityId, view: "tree", ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DepartmentTreeResponse>();
        ok.Value.Should().Be(treeResponse);
    }

    [Fact]
    public async Task List_ForbiddenResult_ReturnsProblem403()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Forbidden("Forbidden context."));

        var result = await _sut.List(_legalEntityId, ct: CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(403);
    }

    [Fact]
    public void List_HasNoTenantIdOrHeadPositionIdParameter()
    {
        var listMethod = typeof(DepartmentsController).GetMethod(nameof(DepartmentsController.List));
        var parameterNames = listMethod!.GetParameters().Select(p => p.Name).ToList();

        parameterNames.Should().NotContain(name => string.Equals(name, "tenantId", StringComparison.OrdinalIgnoreCase));
        parameterNames.Should().NotContain(name => string.Equals(name, "headPositionId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_SendsQuery_WithRouteIds_AndReturnsOk()
    {
        var sample = SampleDepartmentResponse(_legalEntityId, _departmentId, "Engineering");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetDepartmentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Success(sample));

        var result = await _sut.Get(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<GetDepartmentQuery>(q => q.LegalEntityId == _legalEntityId && q.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(sample);
    }

    [Fact]
    public async Task Get_NotFoundResult_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetDepartmentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.NotFound("Department not found."));

        var result = await _sut.Get(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_MapsRequestBodyAndRouteLegalEntityId_IntoCommand_AndReturnsCreatedAtAction()
    {
        var created = SampleDepartmentResponse(_legalEntityId, _departmentId, "Finance");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Success(created));

        var request = new CreateDepartmentRequest("Finance", "FIN", null, null);

        var result = await _sut.Create(_legalEntityId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CreateDepartmentCommand>(c =>
                c.LegalEntityId == _legalEntityId &&
                c.Name == "Finance" &&
                c.Code == "FIN" &&
                c.ParentDepartmentId == null),
            It.IsAny<CancellationToken>()), Times.Once);

        var createdAtResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdAtResult.ActionName.Should().Be(nameof(DepartmentsController.Get));
        createdAtResult.RouteValues!["legalEntityId"].Should().Be(_legalEntityId);
        createdAtResult.RouteValues!["departmentId"].Should().Be(created.Id);
        createdAtResult.Value.Should().Be(created);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsProblem409()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Conflict("Department name already exists in this legal entity."));

        var request = new CreateDepartmentRequest("Finance", "FIN", null, null);

        var result = await _sut.Create(_legalEntityId, request, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_MapsRequestBodyAndRouteIds_IntoCommand_AndReturnsOk()
    {
        var updated = SampleDepartmentResponse(_legalEntityId, _departmentId, "Software Engineering");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Success(updated));

        var request = new UpdateDepartmentRequest("Software Engineering", "SWE", null, null);

        var result = await _sut.Update(_legalEntityId, _departmentId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<UpdateDepartmentCommand>(c =>
                c.LegalEntityId == _legalEntityId &&
                c.DepartmentId == _departmentId &&
                c.Name == "Software Engineering" &&
                c.Code == "SWE" &&
                c.ParentDepartmentId == null),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(updated);
    }

    [Fact]
    public async Task Update_Conflict_ReturnsProblem409()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Conflict("Department cannot be its own parent."));

        var request = new UpdateDepartmentRequest("Software Engineering", "SWE", _departmentId, null);

        var result = await _sut.Update(_legalEntityId, _departmentId, request, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Delete_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Delete(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ArchiveDepartmentCommand>(c => c.LegalEntityId == _legalEntityId && c.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Department not found."));

        var result = await _sut.Delete(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Archive_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Archive(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ArchiveDepartmentCommand>(c => c.LegalEntityId == _legalEntityId && c.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Archive_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ArchiveDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Department not found."));

        var result = await _sut.Archive(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ArchiveCheck_SendsQuery_WithRouteIds_AndReturnsOk()
    {
        var response = new DepartmentArchiveDependencyResponse(
            _departmentId, true,
            new DepartmentArchiveBlockers(0, 0, 0, false, false, false, false),
            "No active employees, positions, or subdepartments are linked to this department.");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CheckDepartmentArchiveDependenciesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentArchiveDependencyResponse>.Success(response));

        var result = await _sut.ArchiveCheck(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CheckDepartmentArchiveDependenciesQuery>(q =>
                q.LegalEntityId == _legalEntityId && q.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(response);
    }

    [Fact]
    public async Task ArchiveCheck_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CheckDepartmentArchiveDependenciesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentArchiveDependencyResponse>.NotFound("Department not found."));

        var result = await _sut.ArchiveCheck(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Restore_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestoreDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Restore(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<RestoreDepartmentCommand>(c => c.LegalEntityId == _legalEntityId && c.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Restore_ParentInactive_ReturnsProblem409()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestoreDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Conflict("Cannot restore: the parent department is missing or inactive. Restore or reassign the parent first."));

        var result = await _sut.Restore(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestoreDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Department not found."));

        var result = await _sut.Restore(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    // CreateAndUpdateRequests_DoNotExposeHeadPositionId was removed here: it asserted the
    // request contracts had no HeadPositionId property, which was correct for Part 2C scope
    // but is now obsolete now that Part 3 legitimately adds Guid? HeadPositionId to both
    // CreateDepartmentRequest and UpdateDepartmentRequest. Coverage for the pass-through now
    // lives in Create_MapsHeadPositionId_IntoCommand and Update_MapsHeadPositionId_IntoCommand
    // below, and the tenantId/legalEntityId exclusion still lives in
    // DepartmentsControllerArchitectureTests.RequestContracts_DoNotExposeTenantId_OrLegalEntityId.

    [Fact]
    public async Task Create_MapsHeadPositionId_IntoCommand()
    {
        var created = SampleDepartmentResponse(_legalEntityId, _departmentId, "Finance");
        var headPositionId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Success(created));

        var request = new CreateDepartmentRequest("Finance", "FIN", null, headPositionId);

        await _sut.Create(_legalEntityId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CreateDepartmentCommand>(c => c.HeadPositionId == headPositionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_MapsHeadPositionId_IntoCommand()
    {
        var updated = SampleDepartmentResponse(_legalEntityId, _departmentId, "Software Engineering");
        var headPositionId = Guid.NewGuid();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentResponse>.Success(updated));

        var request = new UpdateDepartmentRequest("Software Engineering", "SWE", null, headPositionId);

        await _sut.Update(_legalEntityId, _departmentId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<UpdateDepartmentCommand>(c => c.HeadPositionId == headPositionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
