using ONEVO.Application.Features.Auth.Login.Commands.AdminLogin;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class PlatformUserCredentialArchitectureTests
{
    [Fact]
    public void Admin_login_handler_uses_repository_not_config_or_ef_context()
    {
        var parameters = typeof(AdminLoginCommandHandler).GetConstructors()
            .SelectMany(value => value.GetParameters())
            .Select(value => value.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IPlatformUserCredentialRepository), parameters);
        Assert.DoesNotContain(parameters, value => value.Name.Contains("Configuration", StringComparison.Ordinal));
        Assert.DoesNotContain(parameters, value => value.Name.Contains("Environment", StringComparison.Ordinal));
        Assert.DoesNotContain(parameters, value => value.Name.Contains("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public void Platform_user_api_dtos_do_not_expose_credential_hashes()
    {
        var responseTypes = new[]
        {
            typeof(PlatformUserResponse),
            typeof(PlatformUserDetailResponse)
        };
        var propertyNames = responseTypes
            .SelectMany(value => value.GetProperties())
            .Select(value => value.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, value => value.Contains("PasswordHash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, value => value.Contains("ResetToken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, value => value.Contains("CredentialHash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Credential_entity_contains_documented_fields_only()
    {
        var expected = new[]
        {
            "Id", "PlatformUserId", "CredentialType", "PasswordHash", "PasswordAlgorithm",
            "PasswordChangedAt", "MustChangePassword", "FailedLoginCount", "LockedUntil",
            "ResetTokenHash", "ResetTokenExpiresAt", "LastUsedAt", "RevokedAt", "CreatedAt", "UpdatedAt"
        };
        var actual = typeof(PlatformUserCredential).GetProperties()
            .Select(value => value.Name)
            .ToArray();

        Assert.Equal(expected.OrderBy(value => value), actual.OrderBy(value => value));
    }
}
