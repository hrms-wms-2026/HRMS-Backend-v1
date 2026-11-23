using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Features.Auth.Invite.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Tests.Architecture.Security;

/// <summary>
/// Task 44 guard 1: response DTOs and view models must not carry secrets or internal ids.
/// A property whose name matches a sensitive term fails the test unless it is [JsonIgnore]
/// (never serialized) or listed in <see cref="AllowList"/> with a written reason.
/// See docs/security/data-classification.md.
/// </summary>
public sealed class ResponseDtoSensitiveFieldArchitectureTests
{
    // Internal ids: matched when the property name ends with the term (TenantId, ApproverUserId, ...).
    private static readonly string[] InternalIdSuffixes = ["TenantId", "UserId"];

    // Secrets: matched when the property name contains the term (PasswordHash, AccessToken, ...).
    // Boolean properties are skipped for these terms: a flag such as HasAccessToken cannot carry the secret.
    private static readonly string[] SecretTerms =
        ["Password", "Hash", "Salt", "Secret", "Token", "ApiKey", "ConnectionString", "Encrypted"];

    /// <summary>
    /// "TypeName" allows every flagged property on the type; "TypeName.Property" allows one property.
    /// Every entry needs a reason. Do not add an entry to make a new leak pass.
    /// </summary>
    private static readonly Dictionary<string, string> AllowList = new(StringComparer.Ordinal)
    {
        // ── Needed by design ─────────────────────────────────────────────────────────────
        ["MfaSetupDto.Secret"] = "The user must see the TOTP secret once to set up MFA.",
        ["TrayAuthResponseDto"] = "Tray app tokens; the tray app has no cookie jar.",
        ["EnrollmentAttemptResponse"] = "Short-lived AWS credentials for the tray app's biometric enrollment upload.",
        ["LoginResponseDto"] = "Internal handler result; the controller maps it to cookies and a session view, never serialized.",
        ["AdminLoginResultDto"] = "Internal handler result; the admin controller maps it to cookies, never serialized.",
        ["UserDto.TenantId"] = "Internal IUserService result; no controller returns it.",
        ["FileUploadReservationDto.TenantId"] = "Internal IFileStorageService result; no controller returns it.",
        ["ChecklistAssigneeViewModel.UserId"] =
            "The checklist API takes the assignee's user id as input (assignedToUserId); the picker must send it back.",

        // ── Not secrets: names match a term but the value is not one ─────────────────────
        ["PendingLegalDocumentViewModel.ContentHash"] = "Integrity hash of a published legal document, not a credential.",
        ["LegalDocumentVersionDetailDto.ContentHash"] = "Integrity hash of a legal document version (admin app).",
        ["LegalDocumentVersionSummaryDto.ContentHash"] = "Integrity hash of a legal document version (admin app).",
        ["PublishedLegalDocumentDto.ContentHash"] = "Integrity hash of a published legal document.",
        ["AppUsageSnapshotDto.WindowTitleHash"] = "One-way hash that stands in for a window title; it protects privacy, it is not a credential.",
        ["ModuleCatalogDetailDto.AiTokenReference"] = "AI usage token budget reference (admin app), not a credential.",
        ["PlatformOAuthAppDto.TokenUrl"] = "Public OAuth token endpoint URL (admin app), not a token.",
        ["UserIntegrationConnectionDto.ProviderUserId"] = "The user's own id at the external provider (GitHub), not an ONEVO id.",

        // ── Platform admin app: admins work on tenants and platform users by id ──────────
        ["AdminSessionResponseDto.PlatformUserId"] = "Admin app session; platform admin's own id.",
        ["BillingAuditLogDto"] = "Admin app billing audit log.",
        ["InvoiceDetailDto.TenantId"] = "Admin app billing.",
        ["InvoiceSummaryDto.TenantId"] = "Admin app billing.",
        ["TenantSubscriptionDetailDto.TenantId"] = "Admin app billing.",
        ["TenantConfigurationTemplateApplicationDto.TenantId"] = "Admin app configuration templates.",
        ["PlatformAuthEventResponse.UserId"] = "Admin app platform access audit.",
        ["PlatformUserSessionResponse.UserId"] = "Admin app platform access sessions.",
        ["CreateTenantDraftResponseDto.TenantId"] = "Admin app tenant provisioning.",
        ["OwnerInviteResultDto.UserId"] = "Admin app tenant provisioning.",
        ["ProvisioningSummaryDto.TenantId"] = "Admin app tenant provisioning.",
        ["TenantAuditLogEntryResponse.UserId"] = "Admin app tenant audit log.",
        ["TenantInvitationDto.UserId"] = "Admin app tenant invitations.",
        ["TenantSessionResponse.UserId"] = "Admin app tenant session management.",
        ["TenantIntegrationCredentialDto"] = "Admin app (TenantIntegrationCredentialsController); tokens are returned as HasAccessToken flags.",
    };

    private static readonly Assembly ApplicationAssembly = typeof(InvitationDetailDto).Assembly;
    private static readonly Assembly ApiAssembly = typeof(AuthMfaController).Assembly;

    [Fact]
    public void ResponseDtosAndViewModels_DoNotExposeSecretsOrInternalIds()
    {
        var violations = ResponseTypes()
            .SelectMany(FlaggedProperties)
            .Where(v => !IsAllowed(v.Type, v.Property))
            .Select(v => $"{v.Type.FullName}.{v.Property.Name}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, violations).Should().BeEmpty(
            "response DTOs must not carry secrets or internal ids (TenantId, UserId). " +
            "Use EmployeeId or the workspace slug, mark the property [JsonIgnore] if it is never serialized, " +
            "or add an allow-list entry with a written reason. See docs/security/data-classification.md");
    }

    [Fact]
    public void AllowList_EntriesStillMatchAFlaggedProperty()
    {
        var flagged = ResponseTypes()
            .SelectMany(FlaggedProperties)
            .SelectMany(v => new[] { TypeKey(v.Type), $"{TypeKey(v.Type)}.{v.Property.Name}" })
            .ToHashSet(StringComparer.Ordinal);

        AllowList.Keys.Where(k => !flagged.Contains(k)).Should().BeEmpty(
            "a stale allow-list entry hides nothing and should be removed");
        AllowList.Values.Should().OnlyContain(reason => !string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Scanner_FlagsTenantIdAddedToATenantResponseDto()
    {
        FlaggedProperties(typeof(LeakyResponseProbe)).Select(v => v.Property.Name)
            .Should().BeEquivalentTo(nameof(LeakyResponseProbe.TenantId), nameof(LeakyResponseProbe.RefreshToken));
        FlaggedProperties(typeof(SafeResponseProbe)).Should().BeEmpty();
    }

    [Fact]
    public void InvitationPreview_IsAnonymous_AndCarriesNoInternalIds()
    {
        // GET /api/v1/auth/invitations/{token} is [AllowAnonymous]: anyone holding the email link sees it.
        JsonFieldNames(typeof(InvitationDetailDto)).Should().BeEquivalentTo(
            "tenant_name", "invited_email", "first_name", "last_name", "role_name", "expires_at", "status",
            "password_setup_enabled", "google_sign_in_enabled", "allow_google_email_mismatch",
            "allowed_email_domains", "requires_password");
    }

    [Theory]
    [InlineData(typeof(AttendanceCorrectionApproverResponse))]
    [InlineData(typeof(WorkAreaChangeApproverResponse))]
    public void AttendanceApproverResponses_UseEmployeeIdOnly(Type approverType)
    {
        approverType.GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("EmployeeId", "DisplayName", "PositionName");
    }

    [Fact]
    public void Scanner_CoversTheKnownResponseTypes()
    {
        var types = ResponseTypes().ToList();
        types.Should().Contain(typeof(InvitationDetailDto));
        types.Should().Contain(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal) && t.Assembly == ApiAssembly);
    }

    private static IEnumerable<Type> ResponseTypes()
    {
        var dtos = ApplicationAssembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.Contains(".DTOs.Responses", StringComparison.Ordinal));
        var viewModels = ApiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal));
        return dtos.Concat(viewModels)
            .Where(t => t is { IsClass: true } or { IsValueType: true, IsEnum: false })
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            .Distinct();
    }

    private static IEnumerable<(Type Type, PropertyInfo Property)> FlaggedProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => !p.IsDefined(typeof(JsonIgnoreAttribute), true))
            .Where(IsSensitiveName)
            .Select(p => (type, p));

    private static bool IsSensitiveName(PropertyInfo property)
    {
        var name = property.Name;
        if (InternalIdSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            return true;
        return CanCarrySecretValue(property.PropertyType)
            && SecretTerms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    // A flag, count or timestamp (HasAccessToken, AiTokenLimitPerMonth, TokenExpiresAt,
    // LastPasswordChangedAt) cannot hold the secret itself, so secret terms only apply to
    // properties that can carry a value such as a string or bytes.
    private static bool CanCarrySecretValue(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return !(t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(DateTime)
            || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly)
            || t == typeof(TimeSpan) || t == typeof(Guid));
    }

    private static IReadOnlyList<string> JsonFieldNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !p.IsDefined(typeof(JsonIgnoreAttribute), true))
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
            .ToList();

    private static bool IsAllowed(Type type, PropertyInfo property) =>
        AllowList.ContainsKey(TypeKey(type)) || AllowList.ContainsKey($"{TypeKey(type)}.{property.Name}");

    private static string TypeKey(Type type) => type.Name;

    private sealed record LeakyResponseProbe(Guid Id, Guid TenantId, string RefreshToken, bool HasAccessToken);

    private sealed record SafeResponseProbe(
        Guid Id,
        Guid EmployeeId,
        [property: JsonIgnore] Guid UserId,
        bool HasRefreshToken);
}
