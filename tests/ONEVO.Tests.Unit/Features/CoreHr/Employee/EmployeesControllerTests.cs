using MediatR;
using Moq;
using ONEVO.Api.Contracts.CoreHr.Employees;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmploymentType;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.BulkStartOffboarding;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeIdentity;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;
using Microsoft.AspNetCore.Mvc;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeesControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly EmployeesController _sut;

    public EmployeesControllerTests()
    {
        _sut = new EmployeesController(_mediator.Object);
    }

    [Fact]
    public async Task List_UsesDefaultQueryValues_WhenNoneProvided()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListPageResponse>.Success(new EmployeeListPageResponse([], 0, 1, 25)));

        await _sut.List(ct: CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<ListEmployeesQuery>(q =>
                q.Search == null && q.DepartmentId == null && q.LegalEntityId == null
                && q.Page == 1 && q.PageSize == 25),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_ReturnsOk_WithPagedResponse_OnSuccess()
    {
        var response = new EmployeeListPageResponse([], 0, 1, 25);
        _mediator
            .Setup(m => m.Send(It.IsAny<ListEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListPageResponse>.Success(response));

        var result = await _sut.List(ct: CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, okResult.Value);
    }

    [Fact]
    public async Task List_ReturnsProblem_WithResultStatusCode_OnFailure()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListPageResponse>.Forbidden("nope"));

        var result = await _sut.List(ct: CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsOk_OnSuccess()
    {
        var id = Guid.NewGuid();
        var response = new EmployeeListItemResponse(id, "E-001", "Ada Lovelace", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null);
        _mediator
            .Setup(m => m.Send(It.Is<GetEmployeeQuery>(q => q.EmployeeId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(response));

        var result = await _sut.GetById(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, okResult.Value);
    }

    [Fact]
    public async Task GetById_Returns404_WhenQueryReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.IsAny<GetEmployeeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.NotFound("missing"));

        var result = await _sut.GetById(id, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetIdentity_ReturnsOk_OnSuccess()
    {
        var id = Guid.NewGuid();
        var response = new EmployeeIdentityResponse(id, "Ada Lovelace", null);
        _mediator
            .Setup(m => m.Send(It.Is<GetEmployeeIdentityQuery>(q => q.EmployeeId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeIdentityResponse>.Success(response));

        var result = await _sut.GetIdentity(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, okResult.Value);
    }

    [Fact]
    public async Task GetIdentity_Returns404_WhenQueryReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.IsAny<GetEmployeeIdentityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeIdentityResponse>.NotFound("missing"));

        var result = await _sut.GetIdentity(id, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
    }

    private static string? PermissionOf(string actionName) =>
        typeof(EmployeesController).GetMethod(actionName)!
            .GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(ONEVO.Api.Filters.RequirePermissionAttribute))
            .ConstructorArguments[0].Value as string;

    [Theory]
    [InlineData(nameof(EmployeesController.BulkChangePosition), "employees:write")]
    [InlineData(nameof(EmployeesController.BulkChangeEmploymentType), "employees:write")]
    [InlineData(nameof(EmployeesController.BulkStartOffboarding), "employees:offboard")]
    public void BulkActions_RequireTheSamePermissionAsTheSingleAction(string action, string permission)
        => Assert.Equal(permission, PermissionOf(action));

    [Fact]
    public async Task BulkChangePosition_SendsCommand_AndReturnsOk()
    {
        var ids = new[] { Guid.NewGuid() };
        var positionId = Guid.NewGuid();
        var response = BulkEmployeeActionResponse.From(Array.Empty<BulkEmployeeActionItemResponse>());
        _mediator.Setup(m => m.Send(It.IsAny<BulkChangeEmployeePositionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkEmployeeActionResponse>.Success(response));

        var result = await _sut.BulkChangePosition(
            new BulkChangePositionRequest(ids, positionId, new DateOnly(2026, 10, 1), "Promotion"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        _mediator.Verify(m => m.Send(It.Is<BulkChangeEmployeePositionCommand>(c =>
            c.EmployeeIds.SequenceEqual(ids) && c.PositionId == positionId && c.ChangeReason == "Promotion" && c.ReportsToEmployeeId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkChangeEmploymentType_SendsCommand()
    {
        var ids = new[] { Guid.NewGuid() };
        _mediator.Setup(m => m.Send(It.IsAny<BulkChangeEmploymentTypeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkEmployeeActionResponse>.Success(BulkEmployeeActionResponse.From(Array.Empty<BulkEmployeeActionItemResponse>())));

        var result = await _sut.BulkChangeEmploymentType(new BulkChangeEmploymentTypeRequest(ids, "intern"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _mediator.Verify(m => m.Send(It.Is<BulkChangeEmploymentTypeCommand>(c => c.EmployeeIds.SequenceEqual(ids) && c.EmploymentTypeCode == "intern"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkStartOffboarding_SendsCommand()
    {
        var ids = new[] { Guid.NewGuid() };
        _mediator.Setup(m => m.Send(It.IsAny<BulkStartOffboardingCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkEmployeeActionResponse>.Success(BulkEmployeeActionResponse.From(Array.Empty<BulkEmployeeActionItemResponse>())));

        var result = await _sut.BulkStartOffboarding(
            new BulkStartOffboardingRequest(ids, "retirement", new DateOnly(2026, 12, 31), "high", "eligible", null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _mediator.Verify(m => m.Send(It.Is<BulkStartOffboardingCommand>(c =>
            c.EmployeeIds.SequenceEqual(ids) && c.Reason == "retirement" && c.KnowledgeRiskLevel == "high" && c.RehireEligibility == "eligible"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
