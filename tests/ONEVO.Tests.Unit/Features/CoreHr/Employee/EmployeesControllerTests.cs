using MediatR;
using Moq;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
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
}
