using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;
using ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Proves ResetPasswordCommandValidator and ForcePasswordChangeCommandValidator are actually
/// discovered by the same AddValidatorsFromAssembly scan that ONEVO.Application.DependencyInjection
/// runs in production, so ValidationBehavior (registered globally for every MediatR request) will
/// invoke them at runtime rather than the validators sitting unused. Also proves the exception
/// ValidationBehavior throws for a weak password is the exact FluentValidation.ValidationException
/// type ONEVO.Api.Middleware.ExceptionHandlerMiddleware maps to HTTP 400 - so a weak-password
/// reset-password/force-change-password request fails with 400, not an unhandled 500.
/// </summary>
public sealed class PasswordResetValidationPipelineTests
{
    [Fact]
    public void ResetPasswordCommandValidator_IsDiscoveredByAssemblyScan()
    {
        var provider = BuildProvider();

        var validator = provider.GetService<IValidator<ResetPasswordCommand>>();

        validator.Should().BeOfType<ResetPasswordCommandValidator>();
    }

    [Fact]
    public void ForcePasswordChangeCommandValidator_IsDiscoveredByAssemblyScan()
    {
        var provider = BuildProvider();

        var validator = provider.GetService<IValidator<ForcePasswordChangeCommand>>();

        validator.Should().BeOfType<ForcePasswordChangeCommandValidator>();
    }

    [Fact]
    public async Task ValidationBehavior_WeakResetPassword_ThrowsFluentValidationValidationException()
    {
        var behavior = new ValidationBehavior<ResetPasswordCommand, Result>(
            new IValidator<ResetPasswordCommand>[] { new ResetPasswordCommandValidator() });

        var act = () => behavior.Handle(
            new ResetPasswordCommand("some-token", "short"),
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        // ExceptionHandlerMiddleware pattern-matches on FluentValidation.ValidationException (see
        // its `using FluentValidation;`) and maps it to HTTP 400 - the same type thrown here.
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ValidationBehavior_WeakForcePasswordChange_ThrowsFluentValidationValidationException()
    {
        var behavior = new ValidationBehavior<ForcePasswordChangeCommand, Result<LoginResponseDto>>(
            new IValidator<ForcePasswordChangeCommand>[] { new ForcePasswordChangeCommandValidator() });

        var act = () => behavior.Handle(
            new ForcePasswordChangeCommand("user@acme.test", "OldPassword1", "short", null, null),
            _ => Task.FromResult(Result<LoginResponseDto>.Success(null!)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services.BuildServiceProvider();
    }
}
