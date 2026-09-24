using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.Commands.InviteTenantAdmin;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Invite;

public class InviteTenantAdminCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ITenantOwnerInvitationService> _invitationService = new();

    private static Tenant MakeTenant(TenantStatus status = TenantStatus.Provisioning) => new()
    {
        Id = TenantId,
        Name = "Acme Corp",
        Slug = "acme",
        Status = status
    };

    private static InviteTenantAdminCommand MakeCommand() => new(
        TenantId: TenantId,
        Email: "owner@acme.com",
        FirstName: "Ada",
        LastName: "Owner",
        RoleId: RoleId,
        CompletionMethods: ["password", "google"],
        AllowGoogleEmailMismatch: false,
        AllowedEmailDomains: null);

    private InviteTenantAdminCommandHandler BuildSut() => new(
        _tenants.Object,
        _currentUser.Object,
        _invitationService.Object);

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden_AndNeverLooksUpTenant()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _tenants.Verify(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TenantNotFound_ReturnsNotFound()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _invitationService.Verify(
            s => s.InviteOwnerAsync(It.IsAny<Guid>(), It.IsAny<TenantOwnerInviteRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(TenantStatus.Trial)]
    [InlineData(TenantStatus.Active)]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Cancelled)]
    public async Task Handle_TenantNotInProvisioningStatus_ReturnsConflict(TenantStatus status)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(status));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _invitationService.Verify(
            s => s.InviteOwnerAsync(It.IsAny<Guid>(), It.IsAny<TenantOwnerInviteRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidRequest_DelegatesToInvitationService_WithMappedRequest()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var expected = new TenantInvitationDto(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7));
        _invitationService
            .Setup(s => s.InviteOwnerAsync(
                TenantId,
                It.Is<TenantOwnerInviteRequest>(r =>
                    r.Email == "owner@acme.com" &&
                    r.FirstName == "Ada" &&
                    r.LastName == "Owner" &&
                    r.AllowGoogleEmailMismatch == false &&
                    r.CompletionMethods!.SequenceEqual(new[] { "password", "google" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TenantInvitationDto>.Success(expected));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_InvitationServiceFails_PropagatesFailureUnchanged()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());
        _invitationService
            .Setup(s => s.InviteOwnerAsync(TenantId, It.IsAny<TenantOwnerInviteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TenantInvitationDto>.Conflict("An invitation is already pending for this tenant."));

        var result = await BuildSut().Handle(MakeCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already pending");
    }
}
