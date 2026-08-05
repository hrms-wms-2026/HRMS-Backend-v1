using System.Text.RegularExpressions;
using FluentAssertions;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the password-reset production-readiness hardening: atomic token consumption (no more
/// read-then-mark-UsedAt race), no admin-mode/RLS-bypass shortcuts, no raw token/password logging,
/// a single generic error message on every reset-password failure path, and that the process-local
/// rate limiter still covers forgot-password/reset-password/force-change-password.
/// </summary>
public sealed class PasswordResetHardeningArchitectureTests
{
    private static readonly string[] ResetPasswordHandlerPath =
    {
        "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
        "ResetPassword", "ResetPasswordCommandHandler.cs"
    };

    private static readonly string[] ForcePasswordChangeHandlerPath =
    {
        "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
        "ForcePasswordChange", "ForcePasswordChangeCommandHandler.cs"
    };

    [Fact]
    public void ResetPasswordCommandHandler_NoLongerUsesNonAtomicReadThenMarkConsumption()
    {
        var source = ReadSource(ResetPasswordHandlerPath);

        source.Should().NotContain("GetResetTokenByHashAsync",
            "ResetPasswordCommandHandler must consume tokens atomically, not read-then-set-UsedAt");
        source.Should().Contain("TryConsumeResetTokenAsync",
            "ResetPasswordCommandHandler must call the atomic UPDATE ... WHERE used_at IS NULL guard");
    }

    [Fact]
    public void ResetPasswordCommandHandler_WrapsConsumeAndSaveInOneTransaction()
    {
        var source = ReadSource(ResetPasswordHandlerPath);

        source.Should().Contain("ExecuteInTransactionAsync",
            "token consumption and the password/refresh-token/permission-version writes must commit " +
            "or roll back together, not as separate non-atomic SaveChangesAsync calls");
    }

    [Fact]
    public void ResetPasswordCommandHandler_EveryFailureReturnsTheSameGenericErrorLiteral()
    {
        var source = ReadSource(ResetPasswordHandlerPath);

        var failureLiterals = Regex.Matches(source, @"Result\.Failure\(([^,]+),")
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct()
            .ToList();

        failureLiterals.Should().ContainSingle(
            "every Result.Failure(...) call in ResetPasswordCommandHandler must reference the same " +
            "generic error - never a distinct message that would let a caller distinguish an invalid " +
            "token from a missing/inactive user");
    }

    [Theory]
    [MemberData(nameof(PasswordResetHandlerPaths))]
    public void PasswordResetHandlers_NeverUseAdminModeOrDisableRls(string[] path)
    {
        var source = ReadSource(path);

        foreach (var forbidden in new[] { "SetAdminMode", "BYPASSRLS", "DisableRowLevelSecurity" })
        {
            source.Should().NotContain(forbidden,
                $"{path[^1]} must never use {forbidden} to work around RLS");
        }
    }

    [Theory]
    [MemberData(nameof(PasswordResetHandlerPaths))]
    public void PasswordResetHandlers_NeverCallSendPasswordResetAsyncDirectly(string[] path)
    {
        var source = ReadSource(path);

        source.Should().NotContain("SendPasswordResetAsync",
            $"{path[^1]} must never call the email sender directly - delivery goes through the outbox");
    }

    [Fact]
    public void ResetPasswordCommandHandler_NeverLogsAnything()
    {
        var source = ReadSource(ResetPasswordHandlerPath);

        source.Should().NotContain("_logger",
            "ResetPasswordCommandHandler must never log - the reset token, hash, and new password " +
            "all pass through this handler and there is no safe subset to log");
    }

    [Fact]
    public void ForcePasswordChangeCommandHandler_NeverLogsPasswordsOrTokens()
    {
        var source = ReadSource(ForcePasswordChangeHandlerPath);

        source.Should().NotContain("_logger",
            "ForcePasswordChangeCommandHandler receives CurrentPassword and NewPassword in plaintext " +
            "and must never log");
    }

    [Fact]
    public void AuthRateLimitingMiddleware_StillCoversForgotResetAndForceChangePassword()
    {
        var source = ReadSource("src", "ONEVO.Api", "Middleware", "AuthRateLimitingMiddleware.cs");

        foreach (var path in new[]
        {
            "/api/v1/auth/forgot-password",
            "/api/v1/auth/reset-password",
            "/api/v1/auth/force-change-password",
            "/admin/v1/auth/login",
            "/admin/v1/auth/mfa/verify",
            "/admin/v1/auth/forgot-password",
            "/admin/v1/auth/reset-password"
        })
        {
            source.Should().Contain($"\"{path}\"",
                $"the process-local rate limiter must still have a rule for {path}, " +
                "with a leading slash matching how ASP.NET Core reports the real request path " +
                "(a rule string without one silently never matches any real request)");
        }
    }

    [Fact]
    public void AuthPasswordController_ResetPasswordFailureResponse_NeverAddsExtraFields()
    {
        var source = ReadSource(
            "src", "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthPasswordController.cs");

        var methodBody = ExtractMethodBody(source, "ResetPassword");

        // The controller must surface exactly the handler's generic error, not append token/user
        // identifiers of its own.
        methodBody.Should().Contain("Problem(result.Error, statusCode: result.StatusCode ?? 400)");
        var forbidden = new[] { "tenant_id", "user_id", "token_hash", "tenantId", "userId", "tokenHash" };
        foreach (var term in forbidden)
        {
            methodBody.Should().NotContain(term,
                $"the reset-password response body must never carry {term}");
        }
    }

    public static IEnumerable<object[]> PasswordResetHandlerPaths()
    {
        yield return new object[] { ResetPasswordHandlerPath };
        yield return new object[] { ForcePasswordChangeHandlerPath };
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var startIndex = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThan(0, $"{methodName} must exist in the controller");

        var openBraceIndex = source.IndexOf('{', startIndex);
        var depth = 0;
        var i = openBraceIndex;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        return source.Substring(openBraceIndex, i - openBraceIndex + 1);
    }

    private static string ReadSource(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
