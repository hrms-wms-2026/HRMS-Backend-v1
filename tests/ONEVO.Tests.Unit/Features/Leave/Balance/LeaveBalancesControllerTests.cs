using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Balance.Queries.GetMyBalances;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Balance;

public class LeaveBalancesControllerTests
{
    [Fact]
    public async Task My_SendsGetMyBalancesQuery()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetMyBalancesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LeaveBalanceResponse>>.Success([]));
        var controller = new LeaveBalancesController(mediator.Object);

        var response = await controller.My(2026, CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(It.Is<GetMyBalancesQuery>(q => q.Year == 2026), It.IsAny<CancellationToken>()), Times.Once);
    }
}
