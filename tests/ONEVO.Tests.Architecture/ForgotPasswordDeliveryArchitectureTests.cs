using System.Text.RegularExpressions;
using FluentAssertions;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the forgot-password delivery hardening: durable outbox delivery for reset emails,
/// explicit base-domain candidate-overflow handling, no plaintext-sensitive logging, and the
/// stale MFA setup-completion comment cleanup. Source-scan based (like
/// MfaConfirmSetupArchitectureTests.MfaFeatureCode_NeverReferencesSystemConfigOrServiceKeys)
/// because XML doc comments are not preserved in compiled metadata for reflection.
/// </summary>
public sealed class ForgotPasswordDeliveryArchitectureTests
{
    [Fact]
    public void ForgotPasswordResponse_ContainsOnlyTheGenericMessageField()
    {
        var controllerSource = ReadSource(
            "src", "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthPasswordController.cs");

        var methodBody = ExtractMethodBody(controllerSource, "ForgotPassword");

        methodBody.Should().Contain("message = \"If the email exists, a reset link has been sent.\"");

        var forbidden = new[] { "tenant_id", "user_id", "workspace", "token_hash", "password_hash", "reset_token", "tenantId", "userId" };
        foreach (var term in forbidden)
        {
            methodBody.Should().NotContain(term,
                $"the forgot-password response body must never carry {term}");
        }
    }

    [Fact]
    public void BaseForgotPasswordCommandHandler_HasExplicitCandidateOverflowHandling()
    {
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
            "BaseForgotPassword", "BaseForgotPasswordCommandHandler.cs");

        source.Should().Contain("MaxEligibleCandidates = 8",
            "the overflow threshold must mirror IBaseLoginCandidateRepository's documented 9-row (8 real + 1 probe) contract");
        source.Should().Contain("candidateRows.Count > MaxEligibleCandidates",
            "overflow must be checked explicitly before any token or email work begins");

        var overflowBranch = ExtractOverflowBranch(source);
        overflowBranch.Should().Contain("return Result.Success()",
            "overflow must still produce the generic success result the controller always returns");
        overflowBranch.Should().NotContain("AddAsync",
            "overflow must never create a password reset token");
        overflowBranch.Should().NotContain("EnqueueAsync",
            "overflow must never enqueue a reset email");
    }

    [Fact]
    public void BaseForgotPasswordCommandHandler_SwitchesTenantContextBeforeAnyTenantOwnedWrite()
    {
        // Guards the RLS-violation fix: password_reset_tokens and outbox_messages are tenant-owned
        // and RLS-protected, but the base-domain request starts in root/system tenant context (no
        // tenant resolved yet). The handler must inject ITenantContextSwitcher and call
        // SwitchToTenantAsync before it touches any tenant-owned repository for that candidate.
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
            "BaseForgotPassword", "BaseForgotPasswordCommandHandler.cs");

        source.Should().Contain("ITenantContextSwitcher",
            "the handler must depend on ITenantContextSwitcher to switch into each candidate's tenant before writing");

        var loopBody = ExtractCandidateLoopBody(source);
        var switchIndex = loopBody.IndexOf("SwitchToTenantAsync", StringComparison.Ordinal);
        switchIndex.Should().BeGreaterThan(-1,
            "the per-candidate loop must call SwitchToTenantAsync");

        var listIndex = loopBody.IndexOf("ListValidByUserIdAsync", StringComparison.Ordinal);
        var addIndex = loopBody.IndexOf("AddAsync", StringComparison.Ordinal);
        var enqueueIndex = loopBody.IndexOf("EnqueueAsync", StringComparison.Ordinal);
        var saveIndex = loopBody.IndexOf("SaveChangesAsync", StringComparison.Ordinal);

        foreach (var (name, index) in new[]
        {
            ("ListValidByUserIdAsync", listIndex),
            ("AddAsync", addIndex),
            ("EnqueueAsync", enqueueIndex),
            ("SaveChangesAsync", saveIndex)
        })
        {
            index.Should().BeGreaterThan(-1, $"the per-candidate loop must call {name}");
            switchIndex.Should().BeLessThan(index,
                $"SwitchToTenantAsync must happen before {name} so the write lands under the correct tenant's RLS context");
        }
    }

    [Fact]
    public void BaseForgotPasswordCommandHandler_OverflowReturnsBeforeAnyTenantSwitchOrWrite()
    {
        // The overflow branch (candidateRows.Count > MaxEligibleCandidates) must return before the
        // per-candidate loop - and therefore before SwitchToTenantAsync, ListValidByUserIdAsync,
        // AddAsync, EnqueueAsync, and SaveChangesAsync - ever run. ExtractOverflowBranch already
        // proves AddAsync/EnqueueAsync are absent from the overflow branch itself; this test proves
        // the overflow return is positioned before the loop that contains all five calls, so an
        // overflowing request can never touch another tenant's context at all.
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
            "BaseForgotPassword", "BaseForgotPasswordCommandHandler.cs");

        var overflowCheckIndex = source.IndexOf("candidateRows.Count > MaxEligibleCandidates", StringComparison.Ordinal);
        overflowCheckIndex.Should().BeGreaterThan(-1);

        var overflowReturnIndex = source.IndexOf("return Result.Success();", overflowCheckIndex, StringComparison.Ordinal);
        overflowReturnIndex.Should().BeGreaterThan(-1, "overflow must return Result.Success() immediately");

        var loopIndex = source.IndexOf("foreach (var candidate in candidateRows)", StringComparison.Ordinal);
        loopIndex.Should().BeGreaterThan(-1, "expected a per-candidate loop");

        overflowReturnIndex.Should().BeLessThan(loopIndex,
            "the overflow return must be positioned before the per-candidate loop that switches tenants and writes");
    }

    [Fact]
    public void ForgotPasswordHandlers_NeverUseAdminModeOrDisableRls()
    {
        var handlerPaths = new[]
        {
            new[] { "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseForgotPassword", "BaseForgotPasswordCommandHandler.cs" },
            new[] { "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "RequestPasswordReset", "RequestPasswordResetCommandHandler.cs" }
        };

        var forbidden = new[] { "DisableRowLevelSecurity", "BYPASSRLS", "SetAdminMode" };

        foreach (var segments in handlerPaths)
        {
            var source = ReadSource(segments);
            foreach (var term in forbidden)
            {
                source.Should().NotContain(term,
                    $"{segments[^1]} must never use {term} to work around RLS - tenant-owned writes must go through a real tenant switch instead");
            }
        }
    }

    [Fact]
    public void ForgotPasswordHandlers_NeverCallSendPasswordResetAsyncDirectly()
    {
        // Reset emails must go through the durable outbox (IOutboxWriter.EnqueueAsync +
        // PasswordResetEmailOutboxHandler), never a direct/fire-and-forget IEmailService call from
        // the command handler itself.
        var handlerPaths = new[]
        {
            new[] { "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseForgotPassword", "BaseForgotPasswordCommandHandler.cs" },
            new[] { "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "RequestPasswordReset", "RequestPasswordResetCommandHandler.cs" }
        };

        foreach (var segments in handlerPaths)
        {
            var source = ReadSource(segments);
            source.Should().NotContain("SendPasswordResetAsync",
                $"{segments[^1]} must enqueue via IOutboxWriter, never call SendPasswordResetAsync directly");
        }
    }

    [Fact]
    public void ForgotPasswordHandlers_NeverLogPlaintextEmailOrRawResetToken()
    {
        var handlerPaths = new[]
        {
            new[] { "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "BaseForgotPassword", "BaseForgotPasswordCommandHandler.cs" },
            new[] { "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "RequestPasswordReset", "RequestPasswordResetCommandHandler.cs" }
        };

        var logCallPattern = new Regex(@"_logger\.Log\w+\([^;]*?\);", RegexOptions.Singleline);

        foreach (var segments in handlerPaths)
        {
            var source = ReadSource(segments);
            foreach (Match match in logCallPattern.Matches(source))
            {
                match.Value.Should().NotContain("rawToken",
                    $"{segments[^1]} must never log the raw reset token");
                match.Value.Should().NotContain("user.Email",
                    $"{segments[^1]} must never log a plaintext email");
                if (match.Value.Contains("normalizedEmail"))
                {
                    match.Value.Should().Contain("Fingerprint(normalizedEmail)",
                        $"{segments[^1]} may only log the email as a hash fingerprint, never plaintext");
                }
            }
        }
    }

    [Fact]
    public void PasswordResetEmailOutboxHandler_NeverLogsPayloadContents()
    {
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "OutboxHandlers",
            "PasswordResetEmailOutboxHandler.cs");

        source.Should().NotContain("_logger",
            "the outbox handler must not log the decrypted payload (raw token/email) at all");
    }

    [Fact]
    public void AuthMfaController_VerifyMfaSummary_NoLongerClaimsToFinishSetup()
    {
        var source = ReadSource(
            "src", "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthMfaController.cs");

        source.Should().NotContain("finish MFA setup");
        source.Should().Contain("Verify TOTP code for a login MFA challenge.");
    }

    [Fact]
    public void MfaVerify_StillOnlyLoadsVerifiedTotpRecords()
    {
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
            "MfaVerify", "VerifyMfaCommandHandler.cs");

        source.Should().Contain("isVerified: true");
        source.Should().NotContain("isVerified: false");
    }

    [Fact]
    public void ConfirmMfaSetup_RemainsTheOnlyCommandThatMarksPendingSetupVerified()
    {
        var confirmSetupSource = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
            "MfaConfirmSetup", "ConfirmMfaSetupCommandHandler.cs");
        var verifySource = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
            "MfaVerify", "VerifyMfaCommandHandler.cs");
        var enableSource = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands",
            "MfaEnable", "EnableMfaCommandHandler.cs");

        confirmSetupSource.Should().Contain("IsVerified = true");
        verifySource.Should().NotContain("IsVerified = true");
        enableSource.Should().NotContain("IsVerified = true");
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

    private static string ExtractOverflowBranch(string source)
    {
        var startIndex = source.IndexOf("candidateRows.Count > MaxEligibleCandidates", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThan(0);

        // Brace-depth counting (not a naive first '}' search): the LogWarning message literal
        // inside this block contains its own "{EmailHash}" placeholder braces, which must be
        // treated as a balanced pair rather than mistaken for the block's own closing brace.
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

    private static string ExtractCandidateLoopBody(string source)
    {
        var startIndex = source.IndexOf("foreach (var candidate in candidateRows)", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThan(0, "expected a per-candidate loop over candidateRows");

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
