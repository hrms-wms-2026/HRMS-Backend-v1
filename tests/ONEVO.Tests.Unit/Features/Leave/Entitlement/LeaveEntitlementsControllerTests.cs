using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Leave.Entitlements;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.Commands.GenerateEntitlements;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Queries.PreviewGenerateEntitlements;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class LeaveEntitlementsControllerTests
{
    [Fact]
    public async Task Generate_SendsGenerateCommand()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GenerateEntitlementsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveEntitlementGenerationResultResponse>.Success(
                new LeaveEntitlementGenerationResultResponse(2026, 0, 0, 0, [], [], [])));
        var controller = new LeaveEntitlementsController(mediator.Object);

        var response = await controller.Generate(new GenerateEntitlementsRequest(2026, Guid.NewGuid()), CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(It.Is<GenerateEntitlementsCommand>(c => c.Year == 2026), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviewGenerate_SendsQuery()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<PreviewGenerateEntitlementsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveEntitlementGenerationPreviewResponse>.Success(
                new LeaveEntitlementGenerationPreviewResponse(2026, 0, 0, [], [])));
        var controller = new LeaveEntitlementsController(mediator.Object);

        var response = await controller.PreviewGenerate(new GenerateEntitlementsRequest(2026, null), CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(It.Is<PreviewGenerateEntitlementsQuery>(q => q.Year == 2026), It.IsAny<CancellationToken>()), Times.Once);
    }
}
