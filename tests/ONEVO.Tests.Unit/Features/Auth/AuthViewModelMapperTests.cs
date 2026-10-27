using FluentAssertions;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// The browser tells its own messages, reactions and records apart by comparing them with
/// user.employeeId from the session response, so the API view model must carry it through.
/// </summary>
public sealed class AuthViewModelMapperTests
{
    [Fact]
    public void ToViewModel_SerializesTheUsersEmployeeId()
    {
        var employeeId = Guid.NewGuid();
        var dto = Session(new CurrentUserDto(Guid.NewGuid(), Guid.NewGuid(), "owner@acme.test", employeeId, "Dapi Owner"));

        var json = System.Text.Json.JsonSerializer.Serialize(dto.ToViewModel());

        json.Should().Contain($"\"employeeId\":\"{employeeId}\"");
    }

    [Fact]
    public void ToViewModel_SerializesNullEmployeeId_WhenTheAccountHasNoEmployeeRecord()
    {
        var dto = Session(new CurrentUserDto(Guid.NewGuid(), Guid.NewGuid(), "owner@acme.test"));

        var json = System.Text.Json.JsonSerializer.Serialize(dto.ToViewModel());

        json.Should().Contain("\"employeeId\":null");
    }

    private static AuthSessionResponseDto Session(CurrentUserDto user) => new(
        Authenticated: true,
        User: user,
        Permissions: [],
        ActiveModules: [],
        MustChangePassword: false,
        MfaRequired: false);
}
