using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Legal.Commands.AcceptPendingLegalDocuments;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class AuthPendingLegalControllerTests
{
    private static AuthPendingLegalController CreateController(
        Mock<IMediator> mediator,
        string? legalPendingCookie = "challenge-value",
        string? csrfHeader = "csrf-header-value")
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var httpContext = new DefaultHttpContext();
        if (legalPendingCookie is not null)
            httpContext.Request.Headers.Append("Cookie", $"onevo_legal_pending={legalPendingCookie}");
        if (csrfHeader is not null)
            httpContext.Request.Headers["X-CSRF-Token"] = new StringValues(csrfHeader);

        return new AuthPendingLegalController(mediator.Object, environment.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task MissingLegalPendingCookie_Returns401()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator, legalPendingCookie: null);

        var result = await controller.AcceptPendingLegalDocuments(
            new AcceptPendingLegalDocumentsRequest(new List<LegalAcceptanceItemRequest>()),
            CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(401);
        mediator.Verify(
            instance => instance.Send(It.IsAny<AcceptPendingLegalDocumentsCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MissingCsrfHeader_Returns403AndDoesNotCallMediator()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator, csrfHeader: null);

        var result = await controller.AcceptPendingLegalDocuments(
            new AcceptPendingLegalDocumentsRequest(new List<LegalAcceptanceItemRequest>()),
            CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        mediator.Verify(
            instance => instance.Send(It.IsAny<AcceptPendingLegalDocumentsCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BlankCsrfHeader_Returns403AndDoesNotCallMediator()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator, csrfHeader: "   ");

        var result = await controller.AcceptPendingLegalDocuments(
            new AcceptPendingLegalDocumentsRequest(new List<LegalAcceptanceItemRequest>()),
            CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        mediator.Verify(
            instance => instance.Send(It.IsAny<AcceptPendingLegalDocumentsCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidCsrfHeader_PassesHeaderValueToCommand()
    {
        var mediator = new Mock<IMediator>();
        AcceptPendingLegalDocumentsCommand? sentCommand = null;
        mediator
            .Setup(instance => instance.Send(
                It.IsAny<AcceptPendingLegalDocumentsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<LoginResponseDto>>, CancellationToken>(
                (request, _) => sentCommand = (AcceptPendingLegalDocumentsCommand)request)
            .ReturnsAsync(Result<LoginResponseDto>.Failure("irrelevant", 401));

        var controller = CreateController(
            mediator, legalPendingCookie: "challenge-value", csrfHeader: "csrf-header-value");

        await controller.AcceptPendingLegalDocuments(
            new AcceptPendingLegalDocumentsRequest(new List<LegalAcceptanceItemRequest>()),
            CancellationToken.None);

        sentCommand.Should().NotBeNull();
        sentCommand!.LegalChallenge.Should().Be("challenge-value");
        sentCommand.LegalCsrfToken.Should().Be("csrf-header-value");
    }

    [Fact]
    public void LegalPendingCookiePath_MatchesCompleteLoginRoute()
    {
        var source = File.ReadAllText(FindSourceFile(
            "ONEVO.Api", "Controllers", "Tenant", "Auth", "TenantAuthResponseWriter.cs"));

        source.Should().Contain("Path = \"/api/v1/legal/acceptances/complete-login\"");
    }

    private static string FindSourceFile(string project, params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidateRoot = Path.Combine(directory.FullName, "src", project);
            if (Directory.Exists(candidateRoot))
                return Path.Combine(new[] { candidateRoot }.Concat(relativeSegments).ToArray());

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate src/{project} walking up from {AppContext.BaseDirectory}");
    }
}
