using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Features.Auth.Legal.Commands.AcceptPendingLegalDocuments;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class AuthPendingLegalControllerTests
{
    [Fact]
    public async Task MissingLegalPendingCookie_Returns401()
    {
        var mediator = new Mock<IMediator>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var controller = new AuthPendingLegalController(mediator.Object, environment.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.AcceptPendingLegalDocuments(
            new AcceptPendingLegalDocumentsRequest("csrf", new List<LegalAcceptanceItemRequest>()),
            CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(401);
        mediator.Verify(
            instance => instance.Send(It.IsAny<AcceptPendingLegalDocumentsCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
