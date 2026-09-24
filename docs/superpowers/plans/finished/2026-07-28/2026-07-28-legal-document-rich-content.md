# Legal Document Rich Content Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store Terms/Privacy legal document content (JSON/HTML/text) directly in `legal_document_versions`, add Developer Platform admin CRUD+publish/archive endpoints, add public read endpoints so users can read exact content before accepting, and wire `content_endpoint`/`content_hash` into the pending-legal-acceptance flow — without touching unrelated auth/login/MFA/payment/tenant-provisioning behavior.

**Architecture:** Additive migration on the existing `legal_document_versions` table (4 new required columns, backfilled then locked NOT NULL); a small allowlist-first HTML validator and a SHA-256 hasher as static Application-layer helpers (no new dependency); MediatR commands/queries following the `PlatformOAuthApps` feature template; a new Admin controller reusing the existing `platform.compliance.read`/`platform.compliance.manage` permissions; a new anonymous public-read controller (Phase-1-sanctioned simplification — no pending-cookie scoping); and small, additive edits to the existing pending-legal-acceptance DTO/services to surface `content_endpoint`/`content_hash` and optionally verify a client-supplied hash.

**Tech Stack:** .NET (net10.0), EF Core + Npgsql (PostgreSQL), MediatR, xUnit + FluentAssertions + Moq, ArchUnitNET-style reflection tests.

## Global Constraints

- Work only inside `C:\onevoNew\HRMS-Backend-v1`. Do not touch OneVo-HR docs or Postman collections.
- **NEVER run `git commit` or `git push` during this plan.** Leave all changes as uncommitted working-tree modifications. Skip the "commit" step that normally ends each task — do not stage or commit anything.
- Do not change unrelated auth/login/MFA/password-reset/payment/provider/tenant-provisioning behavior. The legal-acceptance flow itself (`LegalAcceptanceChecker`, `LegalAcceptanceSubmissionService`, `SubmitLegalAcceptanceCommandHandler`, the two legal contracts) IS in scope because the spec explicitly requires wiring `content_endpoint`/`content_hash` into it — but touch only the exact lines described in Task 15/16, nothing else in those files.
- Reuse existing `platform.compliance.read` / `platform.compliance.manage` permission codes (`PlatformPermissionCatalog.cs:51-52`) — do not invent `platform.legal_documents.*`. No permission-catalog or seeding changes needed.
- Only Phase 1 document types are supported: `terms`, `privacy_notice`. Do not add `activity_monitoring_notice` (confirmed unused anywhere in `src`).
- Allowed statuses remain exactly `draft`, `published`, `archived` (already free strings on the entity, no enum).
- No new NuGet dependency. No existing HTML sanitizer library in the repo (confirmed via full-repo search) — implement an allowlist-first regex validator locally.
- `dotnet-ef 10.0.7` is installed globally and confirmed working. EF commands use explicit project flags (no `.sln` in this repo):
  `dotnet ef migrations add <Name> --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj -o Migrations`
- Never hand-edit `ApplicationDbContextModelSnapshot.cs` — it must only change via the `dotnet ef migrations add` tool run against the already-updated entity/configuration.
- `legal_document_versions` has **no RLS and no `tenant_id` column** (only `legal_acceptance_records` is tenant-owned/RLS-protected — confirmed in `LegalDocumentVersionConfiguration.cs` and migration `20260724120000_...`). The existing repository (`EfLegalDocumentVersionRepository`) takes no `ITenantContext` dependency and its queries never filter by tenant. This means the new anonymous public-read endpoints can safely query this table with no tenant context resolved — already proven safe by inspection, not something to re-verify at runtime.
- JSON casing for all new/touched Legal DTOs and request contracts is **snake_case via `[property: JsonPropertyName("...")]`**, matching the existing `PendingLegalDocumentDto` / `Contracts/Auth/AcceptPendingLegalDocumentsRequest.cs` convention (the closest existing neighbor), not the camelCase convention used by unrelated `DevPlatform/SystemConfig` DTOs.
- Repository methods are block-bodied (no expression-bodied `=>` members) — mirrors `EfLegalDocumentVersionRepository.cs`'s existing style and the `PlatformOAuthAppsArchitectureTests.EfPlatformOAuthAppRepository_HasNoExpressionBodiedMembers` pattern.
- Request contracts live in `src/ONEVO.Api/Contracts/Admin/Legal/` (new folder) as standalone files — never nested inside a controller (the existing `LegalController.cs` nested records are legacy and are NOT a pattern to copy for new endpoints).

---

## Task 1: Domain entity + EF configuration — add rich content columns

**Files:**
- Modify: `src/ONEVO.Domain/Features/DevPlatform/Compliance/Entities/LegalDocumentVersion.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Compliance/LegalDocumentVersionConfiguration.cs`

**Interfaces:**
- Produces: `LegalDocumentVersion.ContentJson/ContentHtml/ContentText/ContentHash` (all `string`, non-null with `= string.Empty` default) — used by every later task.

- [ ] **Step 1: Add the four properties to the entity**

Current file (18 lines) ends with `UpdatedAt`. Insert before the closing brace:

```csharp
    public string ContentJson { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string ContentText { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
```

- [ ] **Step 2: Add the EF mappings**

In `LegalDocumentVersionConfiguration.Configure`, after the existing `builder.Property(x => x.PublishReason);` line and before the `HasIndex` calls, add:

```csharp
        builder.Property(x => x.ContentJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContentHtml).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContentText).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
```

After the existing `builder.HasIndex(x => new { x.DocumentType, x.Status, x.IsRequired, x.PublishedAt });` line, add:

```csharp
        builder.HasIndex(x => x.ContentHash)
            .HasDatabaseName("ix_legal_document_versions_content_hash");
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src\ONEVO.Domain\ONEVO.Domain.csproj --no-restore --verbosity minimal` then `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --no-restore --verbosity minimal`
Expected: both succeed (other repositories/seeders that construct `LegalDocumentVersion` without these fields still compile because of the `= string.Empty` defaults — they'll just produce empty-string rows until Task 4 fixes the seeder).

Do not commit (see Global Constraints).

---

## Task 2: Bootstrap content constants, hasher, and HTML validator (Application layer, static helpers)

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Helpers/LegalDocumentBootstrapContent.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Helpers/LegalContentHasher.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Helpers/LegalHtmlValidator.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Helpers/LegalDocumentTypes.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/LegalContentHasherTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/LegalHtmlValidatorTests.cs`

**Interfaces:**
- Produces: `LegalContentHasher.ComputeHash(string html) -> string` (lowercase hex SHA-256 of the trimmed input). `LegalHtmlValidator.Validate(string html) -> LegalHtmlValidator.LegalHtmlValidationResult { bool IsValid, string? ErrorMessage }`. `LegalDocumentTypes.Allowed -> IReadOnlyCollection<string>` (`"terms"`, `"privacy_notice"`). `LegalDocumentBootstrapContent.{TermsHtml,TermsText,TermsJson,PrivacyHtml,PrivacyText,PrivacyJson,GenericFallbackHtml,GenericFallbackText,GenericFallbackJson}` — all `const string`. Used by Task 3 (migration), Task 4 (seeder), Task 7/8 (create/update handlers).
- Consumes: nothing (pure, no DI).

- [ ] **Step 1: Write the failing hasher test**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class LegalContentHasherTests
{
    [Fact]
    public void ComputeHash_IsDeterministic_ForSameInput()
    {
        var first = LegalContentHasher.ComputeHash("<p>Hello</p>");
        var second = LegalContentHasher.ComputeHash("<p>Hello</p>");

        first.Should().Be(second);
        first.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_TrimsWhitespace_BeforeHashing()
    {
        var untrimmed = LegalContentHasher.ComputeHash("  <p>Hello</p>  ");
        var trimmed = LegalContentHasher.ComputeHash("<p>Hello</p>");

        untrimmed.Should().Be(trimmed);
    }

    [Fact]
    public void ComputeHash_MatchesKnownBootstrapTermsHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.TermsHtml);

        hash.Should().Be("20eafc33d68ca09f6c921b2ed37a67c9dfcaefb01af419f7cfd0a961d46ea696");
    }

    [Fact]
    public void ComputeHash_MatchesKnownBootstrapPrivacyHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.PrivacyHtml);

        hash.Should().Be("175134229efb70c740267a878bf0f2ae2954d9829fd6890f09c47801c3a6e1d7");
    }

    [Fact]
    public void ComputeHash_MatchesKnownGenericFallbackHash()
    {
        var hash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.GenericFallbackHtml);

        hash.Should().Be("cc9300cc121f9b3485dc29f8989624a5c340dbd7de7af964d3db538ab805fb24");
    }
}
```

- [ ] **Step 2: Run it to verify it fails (types don't exist yet)**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalContentHasherTests" --verbosity minimal`
Expected: build error — `LegalContentHasher`/`LegalDocumentBootstrapContent` do not exist.

- [ ] **Step 3: Create `LegalDocumentBootstrapContent.cs`**

```csharp
namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Single source of truth for the Phase 1 bootstrap legal document bodies. The migration
/// (backfill SQL), the dev seeder, and the hasher unit tests all read from these constants
/// so the stored content_hash can never drift from what actually gets persisted.
/// </summary>
public static class LegalDocumentBootstrapContent
{
    public const string TermsHtml =
        "<h1>ONEVO Terms and Conditions</h1><p>These are the ONEVO Terms and Conditions (Bootstrap Dev). By using ONEVO, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.</p>";

    public const string TermsText =
        "ONEVO Terms and Conditions\n\nThese are the ONEVO Terms and Conditions (Bootstrap Dev). By using ONEVO, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.";

    public const string TermsJson =
        "{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":1},\"content\":[{\"type\":\"text\",\"text\":\"ONEVO Terms and Conditions\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"These are the ONEVO Terms and Conditions (Bootstrap Dev). By using ONEVO, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.\"}]}]}";

    public const string PrivacyHtml =
        "<h1>ONEVO Privacy Notice</h1><p>This is the ONEVO Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how ONEVO collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.</p>";

    public const string PrivacyText =
        "ONEVO Privacy Notice\n\nThis is the ONEVO Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how ONEVO collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.";

    public const string PrivacyJson =
        "{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":1},\"content\":[{\"type\":\"text\",\"text\":\"ONEVO Privacy Notice\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"This is the ONEVO Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how ONEVO collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.\"}]}]}";

    /// <summary>Defensive fallback for any row that is neither the terms nor privacy_notice bootstrap row.</summary>
    public const string GenericFallbackHtml =
        "<p>This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.</p>";

    public const string GenericFallbackText =
        "This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.";

    public const string GenericFallbackJson =
        "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.\"}]}]}";
}
```

- [ ] **Step 4: Create `LegalContentHasher.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Computes the canonical content_hash: SHA-256 over the trimmed content_html, lowercase hex.
/// The frontend never supplies this value - it is always recomputed server-side.
/// </summary>
public static class LegalContentHasher
{
    public static string ComputeHash(string html)
    {
        var normalized = html.Trim();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Run the hasher test again to verify it passes**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalContentHasherTests" --verbosity minimal`
Expected: 5 passed.

- [ ] **Step 6: Write the failing validator test**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class LegalHtmlValidatorTests
{
    [Theory]
    [InlineData("<h1>Title</h1><p>Body <strong>text</strong> and <em>more</em>.</p>")]
    [InlineData("<ul><li>One</li><li>Two</li></ul>")]
    [InlineData("<blockquote>Quoted</blockquote>")]
    [InlineData("<p>Line one<br>Line two</p>")]
    [InlineData("<a href=\"https://example.com\">link</a>")]
    [InlineData("<a href=\"mailto:test@example.com\">mail</a>")]
    [InlineData("<a href=\"/relative/path\">rel</a>")]
    [InlineData("<table><thead><tr><th colspan=\"2\">H</th></tr></thead><tbody><tr><td>1</td><td>2</td></tr></tbody></table>")]
    public void Validate_AllowsSafeFormatting(string html)
    {
        var result = LegalHtmlValidator.Validate(html);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<p onclick=\"alert(1)\">click</p>")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">")]
    [InlineData("<p onload=\"alert(1)\">x</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<object data=\"x\"></object>")]
    [InlineData("<embed src=\"x\">")]
    [InlineData("<style>body{background:url(x)}</style>")]
    [InlineData("<svg onload=\"alert(1)\"></svg>")]
    [InlineData("<div class=\"x\">unlisted tag</div>")]
    [InlineData("<p style=\"color:red\">unlisted attribute</p>")]
    public void Validate_RejectsUnsafeOrUnlistedContent(string html)
    {
        var result = LegalHtmlValidator.Validate(html);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_RejectsEmptyContent()
    {
        var result = LegalHtmlValidator.Validate("   ");

        result.IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalHtmlValidatorTests" --verbosity minimal`
Expected: build error — `LegalHtmlValidator` does not exist.

- [ ] **Step 8: Create `LegalHtmlValidator.cs`** (allowlist-first: unknown tags and unknown attributes are rejected by default, not blacklisted individually)

```csharp
using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Allowlist-first legal document HTML validator. Any tag not explicitly allowed is rejected,
/// and any attribute not explicitly allowed for its tag is rejected - this is what actually
/// blocks &lt;script&gt;, on*= handlers, javascript: URLs, and &lt;iframe|object|embed&gt; (none of
/// them appear in the allowlists below), rather than trying to blacklist each one individually.
/// </summary>
public static class LegalHtmlValidator
{
    public sealed record LegalHtmlValidationResult(bool IsValid, string? ErrorMessage);

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "s",
        "h1", "h2", "h3", "h4",
        "ul", "ol", "li",
        "blockquote",
        "a",
        "table", "thead", "tbody", "tr", "th", "td"
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedAttributesByTag =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new(StringComparer.OrdinalIgnoreCase) { "href", "title" },
            ["td"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" },
            ["th"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" }
        };

    private static readonly HashSet<string> NoAttributesAllowed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TagPattern =
        new(@"<\s*(/)?\s*([a-zA-Z][a-zA-Z0-9]*)((?:\s+[^<>]*)?)/?\s*>", RegexOptions.Compiled);

    private static readonly Regex AttributePattern =
        new(@"([a-zA-Z][a-zA-Z0-9\-]*)\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.Compiled);

    public static LegalHtmlValidationResult Validate(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new LegalHtmlValidationResult(false, "content_html must not be empty.");
        }

        foreach (Match tagMatch in TagPattern.Matches(html))
        {
            var isClosingTag = tagMatch.Groups[1].Value == "/";
            var tagName = tagMatch.Groups[2].Value.ToLowerInvariant();

            if (!AllowedTags.Contains(tagName))
            {
                return new LegalHtmlValidationResult(
                    false, $"Disallowed tag '<{tagName}>' is not permitted in legal document content.");
            }

            if (isClosingTag)
            {
                continue;
            }

            var allowedAttributes = AllowedAttributesByTag.TryGetValue(tagName, out var tagAttributes)
                ? tagAttributes
                : NoAttributesAllowed;

            var attributesText = tagMatch.Groups[3].Value;
            foreach (Match attributeMatch in AttributePattern.Matches(attributesText))
            {
                var attributeName = attributeMatch.Groups[1].Value;
                var attributeValue = attributeMatch.Groups[3].Success
                    ? attributeMatch.Groups[3].Value
                    : attributeMatch.Groups[4].Value;

                if (!allowedAttributes.Contains(attributeName))
                {
                    return new LegalHtmlValidationResult(
                        false, $"Disallowed attribute '{attributeName}' on '<{tagName}>'.");
                }

                if (string.Equals(attributeName, "href", StringComparison.OrdinalIgnoreCase)
                    && !IsSafeHref(attributeValue))
                {
                    return new LegalHtmlValidationResult(
                        false, $"Unsafe href value '{attributeValue}' on '<a>' tag.");
                }
            }
        }

        return new LegalHtmlValidationResult(true, null);
    }

    private static bool IsSafeHref(string href)
    {
        var trimmed = href.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return true;
        }

        var colonIndex = trimmed.IndexOf(':');
        var slashIndex = trimmed.IndexOf('/');
        var hasUnsafeScheme = colonIndex >= 0 && (slashIndex < 0 || colonIndex < slashIndex);

        return !hasUnsafeScheme;
    }
}
```

- [ ] **Step 9: Run the validator test again to verify it passes**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalHtmlValidatorTests" --verbosity minimal`
Expected: all cases pass. If `<table>` case fails, check `colspan` is in the `th`/`td` allowlist above.

- [ ] **Step 10: Create `LegalDocumentTypes.cs`**

```csharp
namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>Phase 1 supported legal document types only. Do not add new types here without a spec update.</summary>
public static class LegalDocumentTypes
{
    public static readonly IReadOnlyCollection<string> Allowed = new[] { "terms", "privacy_notice" };
}
```

Do not commit.

---

## Task 3: EF migration — add columns, backfill, lock NOT NULL

**Files:**
- Create (via tooling, then hand-edit): `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddLegalDocumentRichContent.cs`
- Auto-created: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddLegalDocumentRichContent.Designer.cs`
- Auto-updated: `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: `LegalContentHasher.ComputeHash`, `LegalDocumentBootstrapContent.*` from Task 2.
- Produces: physical columns `content_json jsonb NOT NULL`, `content_html text NOT NULL`, `content_text text NOT NULL`, `content_hash character varying(128) NOT NULL`, plus index `ix_legal_document_versions_content_hash`.

- [ ] **Step 1: Scaffold the migration**

Task 1 already made the entity/config require these columns, so the tool will diff against that target model. Run:

```
dotnet ef migrations add AddLegalDocumentRichContent --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj -o Migrations
```

Expected: three files created/updated (migration `.cs`, `.Designer.cs`, and `ApplicationDbContextModelSnapshot.cs` updated in place). Do not hand-edit the snapshot or Designer file.

- [ ] **Step 2: Replace the generated `Up`/`Down` bodies**

Open the new `<timestamp>_AddLegalDocumentRichContent.cs`. Keep the class name/attributes the tool generated; replace the entire `Up`/`Down` method bodies (and add the private helper) with:

```csharp
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

// ... inside the partial migration class, after the existing usings/namespace:

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_json",
                table: "legal_document_versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_html",
                table: "legal_document_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_text",
                table: "legal_document_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                table: "legal_document_versions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            var termsHash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.TermsHtml);
            var privacyHash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.PrivacyHtml);
            var genericHash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.GenericFallbackHtml);

            migrationBuilder.Sql($@"
                UPDATE legal_document_versions
                SET content_json = {SqlLiteral(LegalDocumentBootstrapContent.TermsJson)}::jsonb,
                    content_html = {SqlLiteral(LegalDocumentBootstrapContent.TermsHtml)},
                    content_text = {SqlLiteral(LegalDocumentBootstrapContent.TermsText)},
                    content_hash = {SqlLiteral(termsHash)}
                WHERE document_type = 'terms' AND version = '1.0';
            ");

            migrationBuilder.Sql($@"
                UPDATE legal_document_versions
                SET content_json = {SqlLiteral(LegalDocumentBootstrapContent.PrivacyJson)}::jsonb,
                    content_html = {SqlLiteral(LegalDocumentBootstrapContent.PrivacyHtml)},
                    content_text = {SqlLiteral(LegalDocumentBootstrapContent.PrivacyText)},
                    content_hash = {SqlLiteral(privacyHash)}
                WHERE document_type = 'privacy_notice' AND version = '1.0';
            ");

            migrationBuilder.Sql($@"
                UPDATE legal_document_versions
                SET content_json = {SqlLiteral(LegalDocumentBootstrapContent.GenericFallbackJson)}::jsonb,
                    content_html = {SqlLiteral(LegalDocumentBootstrapContent.GenericFallbackHtml)},
                    content_text = {SqlLiteral(LegalDocumentBootstrapContent.GenericFallbackText)},
                    content_hash = {SqlLiteral(genericHash)}
                WHERE content_html IS NULL;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "content_json",
                table: "legal_document_versions",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "content_html",
                table: "legal_document_versions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "content_text",
                table: "legal_document_versions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "content_hash",
                table: "legal_document_versions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_content_hash",
                table: "legal_document_versions",
                column: "content_hash");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_legal_document_versions_content_hash",
                table: "legal_document_versions");

            migrationBuilder.DropColumn(name: "content_json", table: "legal_document_versions");
            migrationBuilder.DropColumn(name: "content_html", table: "legal_document_versions");
            migrationBuilder.DropColumn(name: "content_text", table: "legal_document_versions");
            migrationBuilder.DropColumn(name: "content_hash", table: "legal_document_versions");
        }

        private static string SqlLiteral(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }
```

Note: the third `UPDATE ... WHERE content_html IS NULL` is a defensive catch-all so the later `AlterColumn ... nullable: false` calls never fail on some other pre-existing row (e.g. a dev-DB experimentation row) that isn't `terms/1.0` or `privacy_notice/1.0`. The `content_hash` index is non-unique, so a shared fallback hash across multiple such rows is fine.

- [ ] **Step 2: Build to confirm the migration compiles**

Run: `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --no-restore --verbosity minimal`
Expected: success.

- [ ] **Step 3: Verify the snapshot was actually updated by tooling**

Run a quick check that the snapshot file contains the new columns (read-only check, no edits):

```
findstr /C:"content_hash" src\ONEVO.Infrastructure\Migrations\ApplicationDbContextModelSnapshot.cs
```

Expected: at least one match. If none, Step 1's `dotnet ef migrations add` did not run against the Task-1-updated model — re-run Step 1 after confirming Task 1 is actually saved.

Do not commit.

---

## Task 4: Update the dev/test bootstrap seeder with real content

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs` (the two `LegalDocumentVersion` object initializers inside `SeedDevelopmentLegalVersionsAsync`, currently lines 608-621 and 628-641)

**Interfaces:**
- Consumes: `LegalDocumentBootstrapContent.*`, `LegalContentHasher.ComputeHash` from Task 2.

- [ ] **Step 1: Add the using and the four properties to both object initializers**

Add near the top of the file: `using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;`

In the `terms` initializer, add before the closing `});`:

```csharp
                ContentJson = LegalDocumentBootstrapContent.TermsJson,
                ContentHtml = LegalDocumentBootstrapContent.TermsHtml,
                ContentText = LegalDocumentBootstrapContent.TermsText,
                ContentHash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.TermsHtml),
```

In the `privacy_notice` initializer, add before its closing `});`:

```csharp
                ContentJson = LegalDocumentBootstrapContent.PrivacyJson,
                ContentHtml = LegalDocumentBootstrapContent.PrivacyHtml,
                ContentText = LegalDocumentBootstrapContent.PrivacyText,
                ContentHash = LegalContentHasher.ComputeHash(LegalDocumentBootstrapContent.PrivacyHtml),
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --no-restore --verbosity minimal`
Expected: success.

Do not commit.

---

## Task 5: Repository — extend interface + EF implementation

**Files:**
- Modify: `src/ONEVO.Application/Features/DevPlatform/Compliance/RepositoryInterfaces/ILegalDocumentVersionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/Compliance/EfLegalDocumentVersionRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/EfLegalDocumentVersionRepositoryTests.cs` (extend existing file — do not remove the two existing `[Fact]`s)

**Interfaces:**
- Produces: `ListAsync(string? documentType, string? status, ct)`, `GetByIdAsync(Guid id, ct)` (tracked — used by Update/Publish/Archive), `GetPublishedAsync(string documentType, string version, ct)`, `GetCurrentPublishedByDocumentTypeAsync(string documentType, ct)` (tracked — used by Publish to archive the prior published row). `GetCurrentRequiredVersionsAsync` and `GetByDocumentTypeAndVersionAsync` are unchanged — existing callers (`LegalAcceptanceChecker`, `LegalAcceptanceSubmissionService`, `SubmitLegalAcceptanceCommandHandler`) keep working untouched.

- [ ] **Step 1: Write the failing tests (appended to the existing test file)**

Add these `[Fact]` methods inside the existing `EfLegalDocumentVersionRepositoryTests` class (after the two existing tests, before the private helpers):

```csharp
    [Fact]
    public async Task ListAsync_FiltersByDocumentTypeAndStatus()
    {
        await using var db = BuildInMemoryDb();

        var draftTerms = BuildVersion("terms", "dashboard");
        draftTerms.Status = "draft";
        var publishedTerms = BuildVersion("terms", "dashboard");
        var publishedPrivacy = BuildVersion("privacy_notice", "dashboard");

        db.LegalDocumentVersions.AddRange(draftTerms, publishedTerms, publishedPrivacy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var result = await repository.ListAsync("terms", "published", CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(publishedTerms.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTrackedEntity_ForMutation()
    {
        await using var db = BuildInMemoryDb();

        var version = BuildVersion("terms", "dashboard");
        db.LegalDocumentVersions.Add(version);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var found = await repository.GetByIdAsync(version.Id, CancellationToken.None);
        found.Should().NotBeNull();
        found!.Title = "Changed";
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var reloaded = await db.LegalDocumentVersions.FindAsync(version.Id);
        reloaded!.Title.Should().Be("Changed");
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsNull_WhenStatusIsNotPublished()
    {
        await using var db = BuildInMemoryDb();

        var draft = BuildVersion("terms", "dashboard");
        draft.Status = "draft";
        db.LegalDocumentVersions.Add(draft);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var found = await repository.GetPublishedAsync("terms", "1.0", CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentPublishedByDocumentTypeAsync_ReturnsPublishedRow()
    {
        await using var db = BuildInMemoryDb();

        var published = BuildVersion("terms", "dashboard");
        db.LegalDocumentVersions.Add(published);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalDocumentVersionRepository(db, BuildClock());

        var found = await repository.GetCurrentPublishedByDocumentTypeAsync("terms", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(published.Id);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~EfLegalDocumentVersionRepositoryTests" --verbosity minimal`
Expected: build error — the four methods don't exist on the interface/class yet.

- [ ] **Step 3: Extend the interface**

```csharp
public interface ILegalDocumentVersionRepository
{
    Task<IReadOnlyList<LegalDocumentVersion>> GetCurrentRequiredVersionsAsync(CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetByDocumentTypeAndVersionAsync(string documentType, string version, CancellationToken ct = default);
    Task AddAsync(LegalDocumentVersion entity, CancellationToken ct = default);
    Task<IReadOnlyList<LegalDocumentVersion>> ListAsync(string? documentType, string? status, CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetPublishedAsync(string documentType, string version, CancellationToken ct = default);
    Task<LegalDocumentVersion?> GetCurrentPublishedByDocumentTypeAsync(string documentType, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement in `EfLegalDocumentVersionRepository`** (block-bodied, add after the existing `AddAsync` method)

```csharp
    public async Task<IReadOnlyList<LegalDocumentVersion>> ListAsync(
        string? documentType, string? status, CancellationToken ct = default)
    {
        var query = _db.LegalDocumentVersions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            query = query.Where(x => x.DocumentType == documentType);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var results = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return results;
    }

    public async Task<LegalDocumentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.LegalDocumentVersions
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return entity;
    }

    public async Task<LegalDocumentVersion?> GetPublishedAsync(
        string documentType, string version, CancellationToken ct = default)
    {
        var entity = await _db.LegalDocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.DocumentType == documentType && x.Version == version && x.Status == "published",
                ct);

        return entity;
    }

    public async Task<LegalDocumentVersion?> GetCurrentPublishedByDocumentTypeAsync(
        string documentType, CancellationToken ct = default)
    {
        var entity = await _db.LegalDocumentVersions
            .FirstOrDefaultAsync(x => x.DocumentType == documentType && x.Status == "published", ct);

        return entity;
    }
```

`GetByIdAsync` and `GetCurrentPublishedByDocumentTypeAsync` are deliberately tracked (no `.AsNoTracking()`) because Update/Publish/Archive handlers (Tasks 8-10) mutate the returned entity and call `SaveChangesAsync` on the same `DbContext`.

- [ ] **Step 5: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~EfLegalDocumentVersionRepositoryTests" --verbosity minimal`
Expected: 6 passed (2 existing + 4 new).

Do not commit.

---

## Task 6: Application DTOs + mapper

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/DTOs/Responses/LegalDocumentVersionResponses.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Mappers/LegalDocumentVersionMapper.cs`

**Interfaces:**
- Produces: `LegalDocumentVersionSummaryDto`, `LegalDocumentVersionDetailDto`, `PublishedLegalDocumentDto`, and `LegalDocumentVersionMapper.{ToSummaryDto,ToDetailDto,ToPublishedDto}(LegalDocumentVersion)`. Consumed by every command/query handler in Tasks 7-12 and both controllers in Tasks 13-14.

- [ ] **Step 1: Create the response DTOs**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;

/// <summary>Lightweight list-row shape - deliberately excludes content_json/content_html/content_text.</summary>
public sealed record LegalDocumentVersionSummaryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("block_scope")] string BlockScope,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("published_by_id")] Guid? PublishedById,
    [property: JsonPropertyName("content_hash")] string ContentHash,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Full detail shape, including content. Used only by the admin single-version GET.</summary>
public sealed record LegalDocumentVersionDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("block_scope")] string BlockScope,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("published_by_id")] Guid? PublishedById,
    [property: JsonPropertyName("content_json")] JsonElement ContentJson,
    [property: JsonPropertyName("content_html")] string ContentHtml,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("content_hash")] string ContentHash,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Public/tenant read shape for the two content-read endpoints. Never includes tenant/user IDs.</summary>
public sealed record PublishedLegalDocumentDto(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content_html")] string ContentHtml,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("content_hash")] string ContentHash);
```

- [ ] **Step 2: Create the mapper**

```csharp
using System.Text.Json;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Mappers;

public static class LegalDocumentVersionMapper
{
    public static LegalDocumentVersionSummaryDto ToSummaryDto(LegalDocumentVersion entity)
    {
        return new LegalDocumentVersionSummaryDto(
            entity.Id,
            entity.DocumentType,
            entity.Version,
            entity.Title,
            entity.Status,
            entity.IsRequired,
            entity.BlockScope,
            entity.PublishedAt,
            entity.PublishedById,
            entity.ContentHash,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    public static LegalDocumentVersionDetailDto ToDetailDto(LegalDocumentVersion entity)
    {
        using var contentJsonDocument = JsonDocument.Parse(entity.ContentJson);

        return new LegalDocumentVersionDetailDto(
            entity.Id,
            entity.DocumentType,
            entity.Version,
            entity.Title,
            entity.Status,
            entity.IsRequired,
            entity.BlockScope,
            entity.PublishedAt,
            entity.PublishedById,
            contentJsonDocument.RootElement.Clone(),
            entity.ContentHtml,
            entity.ContentText,
            entity.ContentHash,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    public static PublishedLegalDocumentDto ToPublishedDto(LegalDocumentVersion entity)
    {
        return new PublishedLegalDocumentDto(
            entity.DocumentType,
            entity.Version,
            entity.Title,
            entity.ContentHtml,
            entity.ContentText,
            entity.PublishedAt,
            entity.ContentHash);
    }
}
```

`.Clone()` is required: `JsonElement.RootElement` is a view into the `JsonDocument`'s buffer, which is disposed at the end of the `using` block — cloning copies the value out so it survives.

- [ ] **Step 3: Build**

Run: `dotnet build src\ONEVO.Application\ONEVO.Application.csproj --no-restore --verbosity minimal`
Expected: success.

Do not commit.

---

## Task 7: CreateLegalDocumentVersionCommand

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Commands/CreateLegalDocumentVersion/CreateLegalDocumentVersionCommand.cs` (command + handler in one file, mirrors `ConfigurePlatformOAuthAppCommandHandler.cs`)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/CreateLegalDocumentVersionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalDocumentVersionRepository.{GetByDocumentTypeAndVersionAsync,AddAsync}` (Task 5, unchanged existing methods), `IUnitOfWork.SaveChangesAsync`, `IDateTimeProvider.UtcNow`, `LegalDocumentTypes.Allowed`, `LegalHtmlValidator.Validate`, `LegalContentHasher.ComputeHash` (Task 2), `LegalDocumentVersionMapper.ToDetailDto` (Task 6).
- Produces: `CreateLegalDocumentVersionCommand(string DocumentType, string Version, string Title, string ContentJson, string ContentHtml, string ContentText, bool IsRequired, string BlockScope, Guid ActorPlatformUserId) : IRequest<Result<LegalDocumentVersionDetailDto>>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class CreateLegalDocumentVersionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ComputesContentHash_ForValidDraft()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByDocumentTypeAndVersionAsync("terms", "1.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.1", "ONEVO Terms and Conditions",
            "{\"type\":\"doc\"}", "<h1>Terms</h1><p>Body</p>", "Terms\nBody",
            true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHash.Should().Be(LegalContentHasher.ComputeHash("<h1>Terms</h1><p>Body</p>"));
        result.Value.Status.Should().Be("draft");
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("marketing")]
    [InlineData("activity_monitoring_notice")]
    [InlineData("unknown_type")]
    public async Task Handle_RejectsUnsupportedDocumentType(string documentType)
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            documentType, "1.0", "Title", "{}", "<p>x</p>", "x", true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RejectsUnsafeHtml()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.0", "Title", "{}", "<script>alert(1)</script>", "x", true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RejectsDuplicateVersionForSameDocumentType()
    {
        var existing = new LegalDocumentVersion { Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0" };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByDocumentTypeAndVersionAsync("terms", "1.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.0", "Title", "{}", "<p>x</p>", "x", true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~CreateLegalDocumentVersionCommandHandlerTests" --verbosity minimal`
Expected: build error — command/handler don't exist.

- [ ] **Step 3: Implement the command + handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;

/// <summary>
/// Creates a draft legal document version. Never publishes automatically. content_hash is
/// always recomputed server-side from content_html - it is never accepted from the caller.
/// </summary>
public sealed record CreateLegalDocumentVersionCommand(
    string DocumentType,
    string Version,
    string Title,
    string ContentJson,
    string ContentHtml,
    string ContentText,
    bool IsRequired,
    string BlockScope,
    Guid ActorPlatformUserId) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class CreateLegalDocumentVersionCommandHandler
    : IRequestHandler<CreateLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        CreateLegalDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        if (!LegalDocumentTypes.Allowed.Contains(request.DocumentType))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Document type '{request.DocumentType}' is not a supported Phase 1 legal document type.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure("version is required.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure("title is required.", 400);
        }

        var validation = LegalHtmlValidator.Validate(request.ContentHtml);
        if (!validation.IsValid)
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(validation.ErrorMessage!, 400);
        }

        var existing = await _repository.GetByDocumentTypeAndVersionAsync(
            request.DocumentType, request.Version, cancellationToken);
        if (existing is not null)
        {
            return Result<LegalDocumentVersionDetailDto>.Conflict(
                $"Version '{request.Version}' already exists for document type '{request.DocumentType}'.");
        }

        var now = _clock.UtcNow;
        var entity = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentType = request.DocumentType,
            Version = request.Version,
            Title = request.Title.Trim(),
            ContentJson = request.ContentJson,
            ContentHtml = request.ContentHtml,
            ContentText = request.ContentText,
            ContentHash = LegalContentHasher.ComputeHash(request.ContentHtml),
            IsRequired = request.IsRequired,
            BlockScope = request.BlockScope,
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~CreateLegalDocumentVersionCommandHandlerTests" --verbosity minimal`
Expected: 5 passed.

Do not commit.

---

## Task 8: UpdateLegalDocumentVersionCommand (draft-only, immutability enforced)

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Commands/UpdateLegalDocumentVersion/UpdateLegalDocumentVersionCommand.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/UpdateLegalDocumentVersionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalDocumentVersionRepository.GetByIdAsync` (Task 5), `IUnitOfWork.SaveChangesAsync`, `LegalHtmlValidator.Validate`, `LegalContentHasher.ComputeHash`, `LegalDocumentVersionMapper.ToDetailDto`.
- Produces: `UpdateLegalDocumentVersionCommand(Guid Id, string Title, string ContentJson, string ContentHtml, string ContentText, bool IsRequired, string BlockScope) : IRequest<Result<LegalDocumentVersionDetailDto>>`. Note: no `DocumentType`/`Version` parameters — immutable after create, per spec.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class UpdateLegalDocumentVersionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_RecomputesContentHash_ForDraft()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0",
            Status = "draft", Title = "Old", ContentHtml = "<p>Old</p>", ContentHash = "old-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            draft.Id, "New Title", "{}", "<p>New</p>", "New", true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHash.Should().Be(LegalContentHasher.ComputeHash("<p>New</p>"));
        draft.Title.Should().Be("New Title");
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("published")]
    [InlineData("archived")]
    public async Task Handle_RejectsUpdate_WhenNotDraft(string status)
    {
        var version = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = status,
            ContentHtml = "<p>x</p>"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            version.Id, "New Title", "{}", "<p>New</p>", "New", true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenIdMissing()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            Guid.NewGuid(), "Title", "{}", "<p>x</p>", "x", true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~UpdateLegalDocumentVersionCommandHandlerTests" --verbosity minimal`

- [ ] **Step 3: Implement**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;

/// <summary>
/// Edits a draft in place. document_type/version are immutable after create (not accepted
/// here). Published/archived versions reject with 409 - create a new draft instead.
/// </summary>
public sealed record UpdateLegalDocumentVersionCommand(
    Guid Id,
    string Title,
    string ContentJson,
    string ContentHtml,
    string ContentText,
    bool IsRequired,
    string BlockScope) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class UpdateLegalDocumentVersionCommandHandler
    : IRequestHandler<UpdateLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        UpdateLegalDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
        {
            return Result<LegalDocumentVersionDetailDto>.NotFound(
                $"Legal document version '{request.Id}' was not found.");
        }

        if (!string.Equals(entity.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Legal document version '{request.Id}' is '{entity.Status}' and can no longer be edited. Create a new draft version instead.",
                409);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure("title is required.", 400);
        }

        var validation = LegalHtmlValidator.Validate(request.ContentHtml);
        if (!validation.IsValid)
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(validation.ErrorMessage!, 400);
        }

        entity.Title = request.Title.Trim();
        entity.ContentJson = request.ContentJson;
        entity.ContentHtml = request.ContentHtml;
        entity.ContentText = request.ContentText;
        entity.ContentHash = LegalContentHasher.ComputeHash(request.ContentHtml);
        entity.IsRequired = request.IsRequired;
        entity.BlockScope = request.BlockScope;
        entity.UpdatedAt = _clock.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~UpdateLegalDocumentVersionCommandHandlerTests" --verbosity minimal`
Expected: 4 passed.

Do not commit.

---

## Task 9: PublishLegalDocumentVersionCommand (two-phase transaction — archive-then-publish)

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Commands/PublishLegalDocumentVersion/PublishLegalDocumentVersionCommand.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/PublishLegalDocumentVersionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalDocumentVersionRepository.{GetByIdAsync,GetCurrentPublishedByDocumentTypeAsync}` (Task 5), `IUnitOfWork.{ExecuteInTransactionAsync,SaveChangesAsync}`, `LegalDocumentVersionMapper.ToDetailDto`.
- Produces: `PublishLegalDocumentVersionCommand(Guid Id, string? PublishReason, Guid ActorPlatformUserId) : IRequest<Result<LegalDocumentVersionDetailDto>>`.

**Critical constraint:** `legal_document_versions` has a non-deferrable partial unique index `UNIQUE(document_type) WHERE status='published'` (`ix_legal_document_versions_document_type_published`, see `LegalDocumentVersionConfiguration.cs:26-29`). If the old row's `status='archived'` write and the new row's `status='published'` write both land in a single `SaveChangesAsync` batch, EF Core's statement ordering is not guaranteed and Postgres can raise a unique violation. The handler below calls `SaveChangesAsync` **twice** inside one `ExecuteInTransactionAsync`: archive the old row and flush, then publish the new row and flush. This is the one thing the in-memory EF provider used in the unit tests below will NOT catch (it ignores the filtered unique index) — Task 18's integration test is what actually proves this ordering is safe against real Postgres.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class PublishLegalDocumentVersionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static IUnitOfWork BuildPassthroughUnitOfWork()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>((op, ct) => op(ct));
        return uow.Object;
    }

    [Fact]
    public async Task Handle_ArchivesPreviousPublished_AndPublishesNewDraft()
    {
        var oldPublished = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "published",
            ContentHtml = "<p>Old</p>", ContentText = "Old", ContentJson = "{}"
        };
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.1", Status = "draft",
            ContentHtml = "<p>New</p>", ContentText = "New", ContentJson = "{}"
        };

        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        repo.Setup(r => r.GetCurrentPublishedByDocumentTypeAsync("terms", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldPublished);

        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var actorId = Guid.NewGuid();
        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), clock.Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(draft.Id, "Initial baseline", actorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        oldPublished.Status.Should().Be("archived");
        draft.Status.Should().Be("published");
        draft.PublishedAt.Should().Be(Now);
        draft.PublishedById.Should().Be(actorId);
        draft.PublishReason.Should().Be("Initial baseline");
    }

    [Fact]
    public async Task Handle_Publishes_WhenNoPriorPublishedVersionExists()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "draft",
            ContentHtml = "<p>New</p>", ContentText = "New", ContentJson = "{}"
        };

        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        repo.Setup(r => r.GetCurrentPublishedByDocumentTypeAsync("terms", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), clock.Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(draft.Id, null, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        draft.Status.Should().Be("published");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("archived")]
    public async Task Handle_RejectsPublish_WhenNotDraft(string status)
    {
        var version = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = status,
            ContentHtml = "<p>x</p>", ContentText = "x", ContentJson = "{}"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(version.Id, null, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_RejectsPublish_WhenContentIsEmpty()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "draft",
            ContentHtml = "", ContentText = "", ContentJson = ""
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(draft.Id, null, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~PublishLegalDocumentVersionCommandHandlerTests" --verbosity minimal`

- [ ] **Step 3: Implement**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;

/// <summary>
/// Publishes a draft. Because of the non-deferrable partial unique index on
/// (document_type) WHERE status='published', the prior published row for the same
/// document_type must be archived and flushed to the database BEFORE the new row is
/// marked published and flushed - two SaveChangesAsync calls inside one transaction,
/// never one. Getting this ordering wrong only surfaces against real Postgres, not the
/// in-memory EF provider used in unit tests.
/// </summary>
public sealed record PublishLegalDocumentVersionCommand(
    Guid Id,
    string? PublishReason,
    Guid ActorPlatformUserId) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class PublishLegalDocumentVersionCommandHandler
    : IRequestHandler<PublishLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public PublishLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        PublishLegalDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
        {
            return Result<LegalDocumentVersionDetailDto>.NotFound(
                $"Legal document version '{request.Id}' was not found.");
        }

        if (!string.Equals(entity.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Legal document version '{request.Id}' is '{entity.Status}' and cannot be published; only draft versions can be published.",
                409);
        }

        if (string.IsNullOrWhiteSpace(entity.ContentHtml)
            || string.IsNullOrWhiteSpace(entity.ContentText)
            || string.IsNullOrWhiteSpace(entity.ContentJson))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                "Draft content must be non-empty before publishing.", 400);
        }

        var now = _clock.UtcNow;

        await _unitOfWork.ExecuteInTransactionAsync<bool>(async ct =>
        {
            var currentPublished = await _repository.GetCurrentPublishedByDocumentTypeAsync(
                entity.DocumentType, ct);
            if (currentPublished is not null && currentPublished.Id != entity.Id)
            {
                currentPublished.Status = "archived";
                currentPublished.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(ct);
            }

            entity.Status = "published";
            entity.PublishedAt = now;
            entity.PublishedById = request.ActorPlatformUserId;
            entity.PublishReason = request.PublishReason;
            entity.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);

            return true;
        }, cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~PublishLegalDocumentVersionCommandHandlerTests" --verbosity minimal`
Expected: 5 passed.

Do not commit.

---

## Task 10: ArchiveLegalDocumentVersionCommand

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Commands/ArchiveLegalDocumentVersion/ArchiveLegalDocumentVersionCommand.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/ArchiveLegalDocumentVersionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalDocumentVersionRepository.GetByIdAsync`, `IUnitOfWork.SaveChangesAsync`, `LegalDocumentVersionMapper.ToDetailDto`.
- Produces: `ArchiveLegalDocumentVersionCommand(Guid Id) : IRequest<Result<LegalDocumentVersionDetailDto>>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.ArchiveLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class ArchiveLegalDocumentVersionCommandHandlerTests
{
    [Fact]
    public async Task Handle_ArchivesPublishedVersion_WithoutMutatingContent()
    {
        var version = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "published",
            ContentHtml = "<p>Keep me</p>", ContentHash = "keep-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        var handler = new ArchiveLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var result = await handler.Handle(new ArchiveLegalDocumentVersionCommand(version.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        version.Status.Should().Be("archived");
        version.ContentHtml.Should().Be("<p>Keep me</p>");
        version.ContentHash.Should().Be("keep-hash");
    }

    [Fact]
    public async Task Handle_RejectsArchive_WhenNotPublished()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "draft"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var handler = new ArchiveLegalDocumentVersionCommandHandler(
            repo.Object, new Mock<IUnitOfWork>().Object, new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(new ArchiveLegalDocumentVersionCommand(draft.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ArchiveLegalDocumentVersionCommandHandlerTests" --verbosity minimal`

- [ ] **Step 3: Implement**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.ArchiveLegalDocumentVersion;

/// <summary>Archives a published version. Only status/updated_at change - content body is never touched.</summary>
public sealed record ArchiveLegalDocumentVersionCommand(Guid Id) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class ArchiveLegalDocumentVersionCommandHandler
    : IRequestHandler<ArchiveLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ArchiveLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        ArchiveLegalDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
        {
            return Result<LegalDocumentVersionDetailDto>.NotFound(
                $"Legal document version '{request.Id}' was not found.");
        }

        if (!string.Equals(entity.Status, "published", StringComparison.OrdinalIgnoreCase))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Legal document version '{request.Id}' is '{entity.Status}'; only published versions can be archived.",
                409);
        }

        entity.Status = "archived";
        entity.UpdatedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ArchiveLegalDocumentVersionCommandHandlerTests" --verbosity minimal`
Expected: 2 passed.

Do not commit.

---

## Task 11: Admin queries — ListLegalDocumentVersionsQuery + GetLegalDocumentVersionQuery

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Queries/ListLegalDocumentVersions/ListLegalDocumentVersionsQuery.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Queries/GetLegalDocumentVersion/GetLegalDocumentVersionQuery.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/ListLegalDocumentVersionsQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/GetLegalDocumentVersionQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalDocumentVersionRepository.{ListAsync,GetByIdAsync}` (Task 5), `LegalDocumentVersionMapper.{ToSummaryDto,ToDetailDto}` (Task 6).
- Produces: `ListLegalDocumentVersionsQuery(string? DocumentType, string? Status) : IRequest<Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>>`; `GetLegalDocumentVersionQuery(Guid Id) : IRequest<Result<LegalDocumentVersionDetailDto>>`.

- [ ] **Step 1: Write the failing tests**

```csharp
// ListLegalDocumentVersionsQueryHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.ListLegalDocumentVersions;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class ListLegalDocumentVersionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsSummaryDtos_WithoutContentBody()
    {
        var entity = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Title = "T",
            Status = "draft", ContentHtml = "<p>secret body</p>", ContentJson = "{}",
            ContentText = "secret body", ContentHash = "hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.ListAsync("terms", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { entity });

        var handler = new ListLegalDocumentVersionsQueryHandler(repo.Object);

        var result = await handler.Handle(
            new ListLegalDocumentVersionsQuery("terms", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].ContentHash.Should().Be("hash");
    }
}
```

```csharp
// GetLegalDocumentVersionQueryHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class GetLegalDocumentVersionQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsDetailDto_WithContentFields()
    {
        var entity = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Title = "T",
            Status = "draft", ContentHtml = "<p>Body</p>", ContentJson = "{\"type\":\"doc\"}",
            ContentText = "Body", ContentHash = "hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var handler = new GetLegalDocumentVersionQueryHandler(repo.Object);

        var result = await handler.Handle(new GetLegalDocumentVersionQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHtml.Should().Be("<p>Body</p>");
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenMissing()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var handler = new GetLegalDocumentVersionQueryHandler(repo.Object);

        var result = await handler.Handle(new GetLegalDocumentVersionQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListLegalDocumentVersionsQueryHandlerTests|FullyQualifiedName~GetLegalDocumentVersionQueryHandlerTests" --verbosity minimal`

- [ ] **Step 3: Implement both**

```csharp
// ListLegalDocumentVersionsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.ListLegalDocumentVersions;

/// <summary>Lightweight list - never includes content_json/content_html/content_text.</summary>
public sealed record ListLegalDocumentVersionsQuery(string? DocumentType, string? Status)
    : IRequest<Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>>;

public sealed class ListLegalDocumentVersionsQueryHandler
    : IRequestHandler<ListLegalDocumentVersionsQuery, Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public ListLegalDocumentVersionsQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>> Handle(
        ListLegalDocumentVersionsQuery request,
        CancellationToken cancellationToken)
    {
        var entities = await _repository.ListAsync(request.DocumentType, request.Status, cancellationToken);
        var dtos = entities.Select(LegalDocumentVersionMapper.ToSummaryDto).ToList();

        return Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>.Success(dtos);
    }
}
```

```csharp
// GetLegalDocumentVersionQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetLegalDocumentVersion;

public sealed record GetLegalDocumentVersionQuery(Guid Id) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class GetLegalDocumentVersionQueryHandler
    : IRequestHandler<GetLegalDocumentVersionQuery, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public GetLegalDocumentVersionQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        GetLegalDocumentVersionQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
        {
            return Result<LegalDocumentVersionDetailDto>.NotFound(
                $"Legal document version '{request.Id}' was not found.");
        }

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListLegalDocumentVersionsQueryHandlerTests|FullyQualifiedName~GetLegalDocumentVersionQueryHandlerTests" --verbosity minimal`
Expected: 3 passed.

Do not commit.

---

## Task 12: Public read queries — GetPublishedLegalDocumentQuery + GetCurrentPublishedLegalDocumentsQuery

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Queries/GetPublishedLegalDocument/GetPublishedLegalDocumentQuery.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/Compliance/Queries/GetCurrentPublishedLegalDocuments/GetCurrentPublishedLegalDocumentsQuery.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/GetPublishedLegalDocumentQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Compliance/GetCurrentPublishedLegalDocumentsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalDocumentVersionRepository.GetPublishedAsync` (Task 5, new) and `ILegalDocumentVersionRepository.GetCurrentRequiredVersionsAsync` (existing, unchanged — already filters `status=published && is_required && block_scope=dashboard && published_at<=now`, see `EfLegalDocumentVersionRepository.cs:19-32`). `LegalDocumentVersionMapper.ToPublishedDto`.
- Produces: `GetPublishedLegalDocumentQuery(string DocumentType, string Version) : IRequest<Result<PublishedLegalDocumentDto>>`; `GetCurrentPublishedLegalDocumentsQuery : IRequest<Result<IReadOnlyList<PublishedLegalDocumentDto>>>` (no parameters).

- [ ] **Step 1: Write the failing tests**

```csharp
// GetPublishedLegalDocumentQueryHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetPublishedLegalDocument;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class GetPublishedLegalDocumentQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsDocument_WhenPublished()
    {
        var entity = new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.0", Title = "T", Status = "published",
            ContentHtml = "<p>Body</p>", ContentText = "Body", ContentHash = "hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetPublishedAsync("terms", "1.0", It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var handler = new GetPublishedLegalDocumentQueryHandler(repo.Object);

        var result = await handler.Handle(
            new GetPublishedLegalDocumentQuery("terms", "1.0"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHtml.Should().Be("<p>Body</p>");
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftOrMissing()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetPublishedAsync("terms", "0.9-draft", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var handler = new GetPublishedLegalDocumentQueryHandler(repo.Object);

        var result = await handler.Handle(
            new GetPublishedLegalDocumentQuery("terms", "0.9-draft"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
```

```csharp
// GetCurrentPublishedLegalDocumentsQueryHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetCurrentPublishedLegalDocuments;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class GetCurrentPublishedLegalDocumentsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsOnlyCurrentRequiredDashboardDocuments()
    {
        var terms = new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.0", Title = "Terms", Status = "published",
            ContentHtml = "<p>T</p>", ContentText = "T", ContentHash = "terms-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { terms });

        var handler = new GetCurrentPublishedLegalDocumentsQueryHandler(repo.Object);

        var result = await handler.Handle(new GetCurrentPublishedLegalDocumentsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].ContentHash.Should().Be("terms-hash");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~GetPublishedLegalDocumentQueryHandlerTests|FullyQualifiedName~GetCurrentPublishedLegalDocumentsQueryHandlerTests" --verbosity minimal`

- [ ] **Step 3: Implement both**

```csharp
// GetPublishedLegalDocumentQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetPublishedLegalDocument;

/// <summary>Public read by (document_type, version). Only ever returns status=published rows - never draft/archived.</summary>
public sealed record GetPublishedLegalDocumentQuery(string DocumentType, string Version)
    : IRequest<Result<PublishedLegalDocumentDto>>;

public sealed class GetPublishedLegalDocumentQueryHandler
    : IRequestHandler<GetPublishedLegalDocumentQuery, Result<PublishedLegalDocumentDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public GetPublishedLegalDocumentQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<PublishedLegalDocumentDto>> Handle(
        GetPublishedLegalDocumentQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetPublishedAsync(request.DocumentType, request.Version, cancellationToken);
        if (entity is null)
        {
            return Result<PublishedLegalDocumentDto>.NotFound(
                $"Published document '{request.DocumentType}' version '{request.Version}' was not found.");
        }

        return Result<PublishedLegalDocumentDto>.Success(LegalDocumentVersionMapper.ToPublishedDto(entity));
    }
}
```

```csharp
// GetCurrentPublishedLegalDocumentsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetCurrentPublishedLegalDocuments;

/// <summary>Current published required dashboard-blocking documents (terms/privacy_notice today).</summary>
public sealed record GetCurrentPublishedLegalDocumentsQuery : IRequest<Result<IReadOnlyList<PublishedLegalDocumentDto>>>;

public sealed class GetCurrentPublishedLegalDocumentsQueryHandler
    : IRequestHandler<GetCurrentPublishedLegalDocumentsQuery, Result<IReadOnlyList<PublishedLegalDocumentDto>>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public GetCurrentPublishedLegalDocumentsQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<PublishedLegalDocumentDto>>> Handle(
        GetCurrentPublishedLegalDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var entities = await _repository.GetCurrentRequiredVersionsAsync(cancellationToken);
        var dtos = entities.Select(LegalDocumentVersionMapper.ToPublishedDto).ToList();

        return Result<IReadOnlyList<PublishedLegalDocumentDto>>.Success(dtos);
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~GetPublishedLegalDocumentQueryHandlerTests|FullyQualifiedName~GetCurrentPublishedLegalDocumentsQueryHandlerTests" --verbosity minimal`
Expected: 3 passed.

Do not commit.

---

## Task 13: Admin API contracts + AdminLegalDocumentVersionsController

**Files:**
- Create: `src/ONEVO.Api/Contracts/Admin/Legal/CreateLegalDocumentVersionRequest.cs`
- Create: `src/ONEVO.Api/Contracts/Admin/Legal/UpdateLegalDocumentVersionRequest.cs`
- Create: `src/ONEVO.Api/Contracts/Admin/Legal/PublishLegalDocumentVersionRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Legal/AdminLegalDocumentVersionsController.cs`

**Interfaces:**
- Consumes: all six commands/queries from Tasks 7-12; `PlatformPermissionCatalog.{ComplianceRead,ComplianceManage}` (existing, unchanged); `ICurrentPlatformUserContext.UserId` (same pattern as `PlatformOAuthAppsController.cs:92-93`).
- Mirrors auth pattern from `PlatformOAuthAppsController.cs:42-59` exactly: `[Authorize(Policy = "AdminPolicy")]` at class level, `[RequirePlatformPermission(...)]` per action.

- [ ] **Step 1: Create the three request contracts**

```csharp
// CreateLegalDocumentVersionRequest.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Admin.Legal;

public sealed record CreateLegalDocumentVersionRequest(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content_json")] JsonElement ContentJson,
    [property: JsonPropertyName("content_html")] string ContentHtml,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("block_scope")] string BlockScope);
```

```csharp
// UpdateLegalDocumentVersionRequest.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Admin.Legal;

/// <summary>document_type/version are deliberately absent - immutable after create.</summary>
public sealed record UpdateLegalDocumentVersionRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content_json")] JsonElement ContentJson,
    [property: JsonPropertyName("content_html")] string ContentHtml,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("block_scope")] string BlockScope);
```

```csharp
// PublishLegalDocumentVersionRequest.cs
using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Admin.Legal;

/// <summary>content_hash is deliberately absent - the backend always recomputes it, never accepts it.</summary>
public sealed record PublishLegalDocumentVersionRequest(
    [property: JsonPropertyName("publish_reason")] string? PublishReason);
```

- [ ] **Step 2: Create the controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Admin.Legal;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.ArchiveLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.ListLegalDocumentVersions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Legal;

/// <summary>
/// Developer Platform - legal document version management (draft/edit/publish/archive
/// Terms &amp; Privacy). Published content is immutable: only Update on a draft is allowed;
/// publishing a new draft archives the prior published row for the same document_type
/// inside one transaction (see PublishLegalDocumentVersionCommandHandler).
/// SECURITY: reuses the existing platform.compliance.read/manage permissions - no new
/// permission codes were added for this feature.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminLegalDocumentVersionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public AdminLegalDocumentVersionsController(IMediator mediator, ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet("admin/v1/legal-document-versions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "document_type")] string? documentType,
        [FromQuery(Name = "status")] string? status,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListLegalDocumentVersionsQuery(documentType, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("admin/v1/legal-document-versions/{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLegalDocumentVersionQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/legal-document-versions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLegalDocumentVersionRequest request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await _mediator.Send(new CreateLegalDocumentVersionCommand(
            request.DocumentType,
            request.Version,
            request.Title,
            request.ContentJson.GetRawText(),
            request.ContentHtml,
            request.ContentText,
            request.IsRequired,
            request.BlockScope,
            actorId.Value), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("admin/v1/legal-document-versions/{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateLegalDocumentVersionRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateLegalDocumentVersionCommand(
            id,
            request.Title,
            request.ContentJson.GetRawText(),
            request.ContentHtml,
            request.ContentText,
            request.IsRequired,
            request.BlockScope), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/legal-document-versions/{id:guid}/publish")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Publish(
        Guid id, [FromBody] PublishLegalDocumentVersionRequest request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await _mediator.Send(
            new PublishLegalDocumentVersionCommand(id, request.PublishReason, actorId.Value), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/legal-document-versions/{id:guid}/archive")]
    [RequirePlatformPermission(PlatformPermissionCatalog.ComplianceManage)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ArchiveLegalDocumentVersionCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

Confirm the exact namespace of `ICurrentPlatformUserContext` used by `PlatformOAuthAppsController.cs:6` (`ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces`) before building — copy that using verbatim.

- [ ] **Step 3: Build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: success.

Do not commit.

---

## Task 14: Public content-read controller (anonymous, published-only)

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Legal/LegalDocumentContentController.cs`

**Interfaces:**
- Consumes: `GetCurrentPublishedLegalDocumentsQuery`, `GetPublishedLegalDocumentQuery` (Task 12).

This is the Phase-1-sanctioned simplification from the spec: "Public read of published legal document content by document_type/version... acceptable if Terms/Privacy are public documents." It serves both access modes the spec describes (authenticated tenant user, and pending-login user who has `onevo_legal_pending` but no session) with one anonymous, published-only endpoint pair — there is no tenant/user ID in the response and draft/archived content is never returned (`GetPublishedAsync`/`GetCurrentRequiredVersionsAsync` both hard-filter on `status = 'published'`). This is a **separate controller class** from `LegalController.cs` (which is `[Authorize(Policy = "TenantPolicy")]` at class level) specifically so these two routes can be anonymous without touching that controller's auth.

- [ ] **Step 1: Create the controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetCurrentPublishedLegalDocuments;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetPublishedLegalDocument;

namespace ONEVO.Api.Controllers.Tenant.Legal;

/// <summary>
/// Anonymous read of published legal document content. legal_document_versions has no
/// tenant_id and no RLS policy (only legal_acceptance_records is tenant-owned), so these
/// queries are safe with no tenant context resolved. Never returns draft/archived content
/// or any tenant/user identifier - only status=published rows via GetPublishedAsync /
/// GetCurrentRequiredVersionsAsync, both of which hard-filter on status.
/// </summary>
[ApiController]
public sealed class LegalDocumentContentController : ControllerBase
{
    private readonly IMediator _mediator;

    public LegalDocumentContentController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("api/v1/legal/documents/current")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCurrentPublishedLegalDocumentsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("api/v1/legal/documents/{documentType}/{version}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByVersion(string documentType, string version, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPublishedLegalDocumentQuery(documentType, version), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: success. Route collision check: confirm `api/v1/legal/documents/current` does not collide with `api/v1/legal/documents/{documentType}/{version}` — ASP.NET route matching prefers the literal segment `current` only if a route template `api/v1/legal/documents/current` exists (it does, as its own `[HttpGet]`), so `GET /api/v1/legal/documents/current` resolves to `GetCurrent` and never falls through to `GetByVersion` with `documentType="current"`. No ambiguity.

Do not commit.

---

## Task 15: Wire `content_endpoint`/`content_hash` into the pending-legal-acceptance flow

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Legal/Services/ILegalAcceptanceChecker.cs` (lines 12-18: `PendingLegalDocumentDto`)
- Modify: `src/ONEVO.Application/Features/Auth/Legal/Services/LegalAcceptanceChecker.cs` (lines 58-64: DTO construction inside `CheckAsync`)
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Legal/LegalAcceptanceCheckerTests.cs` (create if it doesn't already exist — check with Glob first; if it exists, add the new `[Fact]` to it instead of creating a duplicate file)

**Interfaces:**
- Produces: `PendingLegalDocumentDto` gains two new required properties: `ContentEndpoint (string)`, `ContentHash (string)`. Every existing constructor call site must be updated (there is exactly one, in `LegalAcceptanceChecker.CheckAsync`).

- [ ] **Step 1: Check whether a test file already exists**

Run: `dir /s /b tests\ONEVO.Tests.Unit\Features\Auth\Legal\LegalAcceptanceCheckerTests.cs 2>nul` (PowerShell: `Get-ChildItem -Recurse -Filter LegalAcceptanceCheckerTests.cs -Path tests\ONEVO.Tests.Unit`). If found, open it and add the new `[Fact]` from Step 2 into the existing class instead of creating a new file with a duplicate class name.

- [ ] **Step 2: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Legal;

public sealed class LegalAcceptanceCheckerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_IncludesContentEndpointAndContentHash_ForPendingDocuments()
    {
        var required = new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.1", Title = "Terms", Status = "published",
            IsRequired = true, BlockScope = "dashboard", PublishedAt = Now.AddDays(-1),
            ContentHash = "abc123"
        };

        var versions = new Mock<ILegalDocumentVersionRepository>();
        versions.Setup(v => v.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { required });

        var acceptances = new Mock<ILegalAcceptanceRepository>();
        acceptances.Setup(a => a.GetUserAcceptancesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalAcceptanceRecord>());

        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var checker = new LegalAcceptanceChecker(versions.Object, acceptances.Object, clock.Object);

        var result = await checker.CheckAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.PendingDocuments.Should().ContainSingle();
        result.PendingDocuments[0].ContentEndpoint.Should().Be("/api/v1/legal/documents/terms/1.1");
        result.PendingDocuments[0].ContentHash.Should().Be("abc123");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalAcceptanceCheckerTests" --verbosity minimal`
Expected: build error — `PendingLegalDocumentDto` has no `ContentEndpoint`/`ContentHash` constructor parameters yet.

- [ ] **Step 4: Extend the DTO**

In `ILegalAcceptanceChecker.cs`, replace the `PendingLegalDocumentDto` record with:

```csharp
public record PendingLegalDocumentDto(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("content_url")] string? ContentUrl,
    [property: JsonPropertyName("content_endpoint")] string ContentEndpoint,
    [property: JsonPropertyName("content_hash")] string ContentHash
);
```

- [ ] **Step 5: Update the construction site in `LegalAcceptanceChecker.CheckAsync`**

Replace the `pendingDocs.Add(...)` block (currently lines 58-64) with:

```csharp
                pendingDocs.Add(new PendingLegalDocumentDto(
                    DocumentType: requiredVer.DocumentType,
                    Version: requiredVer.Version,
                    Title: requiredVer.Title,
                    EffectiveAt: requiredVer.PublishedAt,
                    ContentUrl: requiredVer.ContentUrl,
                    ContentEndpoint: $"/api/v1/legal/documents/{requiredVer.DocumentType}/{requiredVer.Version}",
                    ContentHash: requiredVer.ContentHash));
```

- [ ] **Step 6: Run to verify all pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalAcceptanceCheckerTests" --verbosity minimal`
Expected: passes (plus any pre-existing tests in the same file, if one already existed).

- [ ] **Step 7: Full unit build to catch any other `PendingLegalDocumentDto` construction sites**

Run: `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: success. If any other test constructs `PendingLegalDocumentDto` positionally, add the two new arguments there too (grep confirmed only one production call site; tests may have their own fixture builders).

Do not commit.

---

## Task 16: Optional client-supplied `content_hash` verification on legal acceptance (last, independently droppable)

This task is the spec's "optional: if frontend sends content_hash, verify it matches and reject mismatch" requirement. It is scoped last and kept small on purpose — if it needs to be dropped for time, nothing in Tasks 1-15 depends on it.

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Legal/Commands/SubmitLegalAcceptance/SubmitLegalAcceptanceCommandHandler.cs` (the `LegalAcceptanceItemInput` record on line 10, and the `Handle` method body)
- Modify: `src/ONEVO.Application/Features/Auth/Legal/Services/LegalAcceptanceSubmissionService.cs` (`ValidateAndStageAsync` loop)
- Modify: `src/ONEVO.Api/Contracts/Auth/AcceptPendingLegalDocumentsRequest.cs` (`LegalAcceptanceItemRequest`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Auth/AuthPendingLegalController.cs` (the `items = request.Acceptances.Select(...)` line)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Legal/LegalController.cs` (the nested `AcceptanceItemRequest` record and the `items = request.Acceptances?.Select(...)` line)
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Legal/LegalAcceptanceSubmissionServiceTests.cs` (create if it doesn't exist — check with Glob first, same rule as Task 15)

**Interfaces:**
- Produces: `LegalAcceptanceItemInput` gains a fifth, optional, default-`null` parameter `ContentHash` — this is additive (default value) so it does not break the many existing positional `new LegalAcceptanceItemInput(a, b, c)` call sites elsewhere.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Legal;

public sealed class LegalAcceptanceSubmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static LegalDocumentVersion BuildCurrentTerms(string contentHash)
    {
        return new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.1", Status = "published",
            IsRequired = true, BlockScope = "dashboard", PublishedAt = Now.AddDays(-1),
            ContentHash = contentHash
        };
    }

    [Fact]
    public async Task ValidateAndStageAsync_RejectsMismatchedClientSuppliedContentHash()
    {
        var versions = new Mock<ILegalDocumentVersionRepository>();
        versions.Setup(v => v.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { BuildCurrentTerms("server-hash") });

        var acceptances = new Mock<ILegalAcceptanceRepository>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var service = new LegalAcceptanceSubmissionService(versions.Object, acceptances.Object, clock.Object);

        var items = new List<LegalAcceptanceItemInput>
        {
            new("terms", "1.1", "accepted", ContentHash: "client-supplied-wrong-hash")
        };

        var result = await service.ValidateAndStageAsync(
            Guid.NewGuid(), Guid.NewGuid(), items, requireComplete: false, null, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ValidateAndStageAsync_Accepts_WhenContentHashOmitted()
    {
        var versions = new Mock<ILegalDocumentVersionRepository>();
        versions.Setup(v => v.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { BuildCurrentTerms("server-hash") });

        var acceptances = new Mock<ILegalAcceptanceRepository>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var service = new LegalAcceptanceSubmissionService(versions.Object, acceptances.Object, clock.Object);

        var items = new List<LegalAcceptanceItemInput> { new("terms", "1.1", "accepted") };

        var result = await service.ValidateAndStageAsync(
            Guid.NewGuid(), Guid.NewGuid(), items, requireComplete: false, null, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalAcceptanceSubmissionServiceTests" --verbosity minimal`
Expected: build error — `LegalAcceptanceItemInput` has no `ContentHash` parameter yet.

- [ ] **Step 3: Add the optional parameter**

In `SubmitLegalAcceptanceCommandHandler.cs`, change line 10 from:
```csharp
public record LegalAcceptanceItemInput(string DocumentType, string Version, string Decision);
```
to:
```csharp
public record LegalAcceptanceItemInput(string DocumentType, string Version, string Decision, string? ContentHash = null);
```

- [ ] **Step 4: Add the verification check in `LegalAcceptanceSubmissionService.ValidateAndStageAsync`**

Inside the `foreach (var item in acceptances)` loop, immediately after the existing block that resolves `current` from `requiredByType` (the block that returns the "is not the current required version" failure) and before the `validDecision` check, insert:

```csharp
            if (!string.IsNullOrWhiteSpace(item.ContentHash)
                && !string.Equals(item.ContentHash, current.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Failure(
                    $"content_hash for '{item.DocumentType}' version '{item.Version}' does not match the current published content.",
                    409);
            }
```

- [ ] **Step 5: Add the same check in `SubmitLegalAcceptanceCommandHandler.Handle`**

After the existing `if (!docVer.IsRequired)` block and before the `if (!docVer.PublishedAt.HasValue)` block, insert:

```csharp
            if (!string.IsNullOrWhiteSpace(item.ContentHash)
                && !string.Equals(item.ContentHash, docVer.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Failure(
                    $"content_hash for '{item.DocumentType}' version '{item.Version}' does not match the current published content.",
                    409);
            }
```

- [ ] **Step 6: Run to verify the new tests pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalAcceptanceSubmissionServiceTests" --verbosity minimal`
Expected: 2 passed.

- [ ] **Step 7: Thread the optional field through both contracts and both controllers**

In `Contracts/Auth/AcceptPendingLegalDocumentsRequest.cs`, change `LegalAcceptanceItemRequest` to:
```csharp
public record LegalAcceptanceItemRequest(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("content_hash")] string? ContentHash = null);
```

In `AuthPendingLegalController.cs`, change the `items` projection to:
```csharp
        var items = request.Acceptances
            .Select(x => new LegalAcceptanceItemInput(x.DocumentType, x.Version, x.Decision, x.ContentHash))
            .ToList();
```

In `LegalController.cs`, change the nested `AcceptanceItemRequest` to:
```csharp
    public record AcceptanceItemRequest(
        [property: JsonPropertyName("document_type")] string DocumentType,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("decision")] string Decision,
        [property: JsonPropertyName("content_hash")] string? ContentHash = null
    );
```
and its `items` projection to:
```csharp
        var items = request.Acceptances?
            .Select(x => new LegalAcceptanceItemInput(x.DocumentType, x.Version, x.Decision, x.ContentHash))
            .ToList() ?? [];
```

- [ ] **Step 8: Full unit build + targeted test run**

Run: `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalAcceptance" --verbosity minimal`
Expected: both succeed. This filter also re-runs the pre-existing `SubmitLegalAcceptanceCommandHandler`/`AcceptPendingLegalDocumentsCommandHandler` tests (if any) to confirm the new optional parameter didn't break them.

Do not commit.

---

## Task 17: Architecture tests

**Files:**
- Create: `tests/ONEVO.Tests.Architecture/LegalDocumentRichContentArchitectureTests.cs`

**Interfaces:**
- Consumes (via reflection only, no runtime instantiation): `LegalDocumentVersionSummaryDto`, `PendingLegalDocumentDto`, `CreateLegalDocumentVersionCommand`, `UpdateLegalDocumentVersionCommand`, `AdminLegalDocumentVersionsController`, `LegalDocumentContentController` — all from prior tasks.

Per the plan's earlier split: **behavioral** checks (update rejects a published version, the published-query returns 404 for a draft) already live in Unit tests (Tasks 8, 9, 12) — they need a mocked repository to exercise, which isn't what an architecture test does. The checks below are purely **static/reflection-based**, matching the existing `PlatformOAuthAppsArchitectureTests.cs` style.

- [ ] **Step 1: Write the test file**

```csharp
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Static architecture guarantees for the legal document rich content feature:
/// - The admin list DTO never carries content_json/content_html/content_text.
/// - Create/Update commands never accept a client-supplied content_hash.
/// - PendingLegalDocumentDto exposes content_endpoint (no content_url-only behavior remains
///   for required documents).
/// - The two new controllers define no nested request/response record types.
/// </summary>
public class LegalDocumentRichContentArchitectureTests
{
    [Fact]
    public void SummaryDto_DoesNotExpose_ContentBodyProperties()
    {
        var summaryType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses.LegalDocumentVersionSummaryDto);
        var propertyNames = summaryType.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("ContentJson", propertyNames);
        Assert.DoesNotContain("ContentHtml", propertyNames);
        Assert.DoesNotContain("ContentText", propertyNames);
    }

    [Fact]
    public void CreateCommand_NeverAcceptsClientSuppliedContentHash()
    {
        var commandType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion.CreateLegalDocumentVersionCommand);
        var constructorParamNames = commandType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name);

        Assert.DoesNotContain("ContentHash", constructorParamNames);
        Assert.DoesNotContain("contentHash", constructorParamNames);
    }

    [Fact]
    public void UpdateCommand_NeverAcceptsClientSuppliedContentHash()
    {
        var commandType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion.UpdateLegalDocumentVersionCommand);
        var constructorParamNames = commandType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name);

        Assert.DoesNotContain("ContentHash", constructorParamNames);
        Assert.DoesNotContain("contentHash", constructorParamNames);
    }

    [Fact]
    public void UpdateCommand_NeverAcceptsDocumentTypeOrVersion()
    {
        // document_type/version are immutable after create - Update must not be able to change them.
        var commandType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion.UpdateLegalDocumentVersionCommand);
        var constructorParamNames = commandType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name)
            .ToHashSet();

        Assert.DoesNotContain("DocumentType", constructorParamNames);
        Assert.DoesNotContain("Version", constructorParamNames);
    }

    [Fact]
    public void PendingLegalDocumentDto_ExposesContentEndpoint_NotContentUrlOnly()
    {
        var dtoType = typeof(ONEVO.Application.Features.Auth.Legal.Services.PendingLegalDocumentDto);
        var propertyNames = dtoType.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Contains("ContentEndpoint", propertyNames);
        Assert.Contains("ContentHash", propertyNames);
    }

    [Fact]
    public void AdminController_DefinesNoNestedRequestRecords()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Admin.DevPlatform.Legal.AdminLegalDocumentVersionsController);
        var nestedTypes = controllerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(nestedTypes);
    }

    [Fact]
    public void PublicContentController_DefinesNoNestedRequestRecords()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Tenant.Legal.LegalDocumentContentController);
        var nestedTypes = controllerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(nestedTypes);
    }

    [Fact]
    public void AdminController_UsesPlatformPermissions_OnEveryAction()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Admin.DevPlatform.Legal.AdminLegalDocumentVersionsController);
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var platformAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePlatformPermissionAttribute");
            Assert.True(platformAttr is not null, $"{method.Name} must be protected by RequirePlatformPermission.");
        }
    }

    [Fact]
    public void PublicContentController_AllowsAnonymousOnEveryAction()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Tenant.Legal.LegalDocumentContentController);
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var allowAnonymous = method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false);
            Assert.NotEmpty(allowAnonymous);
        }
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --filter "FullyQualifiedName~LegalDocumentRichContentArchitectureTests" --verbosity minimal`
Expected: 9 passed. (These can only be run after Tasks 6-15 exist — if run earlier, they'll fail to compile.)

Do not commit.

---

## Task 18: Integration test (Docker-conditional)

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Features/DevPlatform/Compliance/LegalDocumentRichContentIntegrationTests.cs` (check the actual folder name/base-class convention used by an existing integration test first — Glob `tests/ONEVO.Tests.Integration/**/*.cs` for one recent example, e.g. a Tenancy or SystemConfig integration test, and mirror its base class / WebApplicationFactory setup exactly; this plan does not know that base class's exact name since it wasn't inspected during research)

**Interfaces:**
- Exercises the full HTTP surface end-to-end against a real Postgres container: create draft → publish → GET content endpoint → tenant login shows `content_endpoint`/`content_hash` in pending docs → accept → session issued → publish a new version → next login requires acceptance again.

- [ ] **Step 1: Find the integration test base class/harness**

Run: `dir /s /b tests\ONEVO.Tests.Integration\*.cs | findstr /i "Factory Fixture Base"` (or Glob `**/*Factory*.cs`, `**/*Fixture*.cs` under `tests/ONEVO.Tests.Integration`). Open one recent integration test (e.g. anything under `Features/DevPlatform/` or `Features/Auth/`) and note: the base class name, how the Postgres container is started (Testcontainers vs. a fixed connection string), how an admin platform user / auth cookie is obtained in tests, and how a tenant login flow is exercised end-to-end.

- [ ] **Step 2: Write the test using that exact harness pattern**

Structure the test body as this sequence (adapt HTTP client calls/auth setup to whatever the discovered harness provides):

```csharp
// 1. Authenticate as a platform admin (via the harness's existing admin-login helper).
// 2. POST /admin/v1/legal-document-versions with document_type="terms", version="9.9-it",
//    a small safe content_html, and confirm 200 + status="draft" + a non-empty content_hash
//    in the response that was NOT supplied in the request body.
// 3. POST /admin/v1/legal-document-versions/{id}/publish and confirm 200 + status="published"
//    + published_at/published_by_id set, and that the PRIOR published terms row (from the
//    dev bootstrap seeder) is now status="archived" via a follow-up GET on that prior id.
// 4. GET /api/v1/legal/documents/terms/9.9-it with NO auth header and confirm 200 with the
//    same content_html/content_hash - proving the public read endpoint truly requires no
//    tenant/session context.
// 5. Perform a tenant login for a user with a pending legal acceptance and confirm the
//    response's pending documents include content_endpoint == "/api/v1/legal/documents/terms/9.9-it"
//    and content_hash matching step 2/3's value.
// 6. POST the accept-pending-legal endpoint with decision="accepted" for terms/9.9-it and
//    confirm a session is issued (RequiresLegalAcceptance == false or equivalent).
// 7. Publish yet another new terms version (9.10-it) the same way, then log the same user in
//    again and confirm they are now pending again for 9.10-it (proving new-version
//    re-acceptance works end-to-end).
```

Write this as real xUnit `[Fact]` methods (one fact per numbered step is fine, or one long `[Fact]` walking the whole sequence — mirror whatever granularity the existing integration tests in this project use) using the harness's actual `HttpClient`/`WebApplicationFactory` calls, not the pseudocode above — the pseudocode exists only to specify the exact sequence and assertions; do not commit it verbatim as comments in place of real test code.

- [ ] **Step 3: Check Docker availability, then run**

Run: `docker info` (or `docker version`). If it succeeds:
Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~LegalDocumentRichContentIntegrationTests" --verbosity minimal`
Expected: all pass, including the archive-then-publish two-phase-transaction behavior from Task 9 actually succeeding against real Postgres (this is the one thing the in-memory-provider unit tests in Task 9 cannot prove).

If `docker info` fails (no Docker daemon available in this environment), **skip running this test** and record the exact reason ("Docker daemon not available in this execution environment") in the final report's "skipped checks" section — do not mark it as passing, and do not delete the test file.

Do not commit.

---

## Task 19: Full verification run

**Files:** none (verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: 0 errors.

- [ ] **Step 2: Full unit test suite**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal`
Expected: all pass, including every pre-existing test (this proves nothing outside the legal-document feature regressed).
If `--no-build` fails because the prior build step used a different configuration, drop `--no-build` and let it rebuild.

- [ ] **Step 3: Full architecture test suite**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal`
Expected: all pass, including the untouched `LegalAcceptanceMigrationIntegrityTests` (which reads only the OLD `20260724120000_...` migration and must stay green) and `PlatformOAuthAppsArchitectureTests`.

- [ ] **Step 4: Integration suite, conditional**

Already run (or explicitly skipped with a recorded reason) in Task 18, Step 3. Do not re-run here unless Task 18 was skipped and Docker has since become available.

- [ ] **Step 5: Targeted content/security greps**

Run each of these and record the output for the final report:

```
findstr /s /n "content_url.*required" src\*.cs tests\*.cs
findstr /s /n "csrf_token" src\*.cs tests\*.cs
findstr /s /n "<script" src\*.cs tests\*.cs
findstr /s /n "javascript:" src\*.cs tests\*.cs
findstr /s /n "onerror" src\*.cs tests\*.cs
findstr /s /n "onclick" src\*.cs tests\*.cs
findstr /s /n "content_hash" src\*.cs tests\*.cs
```

(Use `rg -n "pattern" src tests` instead if ripgrep is available in this shell — equivalent, just faster.) Expected: the `<script`/`javascript:`/`onerror`/`onclick` hits should only appear inside the validator's own allowlist/blacklist test data (`LegalHtmlValidatorTests.cs`) and the validator's doc-comment — never in production HTML-construction code. `content_hash` hits should span exactly the files this plan touched.

- [ ] **Step 6: Whitespace/diff hygiene check**

Run: `git diff --check`
Expected: no output (no trailing-whitespace or conflict-marker issues introduced). Do not run `git add`/`git commit` regardless of output — see Global Constraints.

Do not commit.

---

## Task 20: Final report

**Files:**
- Create: `LEGAL_DOCUMENT_RICH_CONTENT_MANAGEMENT_REPORT.md` (repo root of `HRMS-Backend-v1`)

- [ ] **Step 1: Write the report**

Include these sections, each populated with the actual results observed while executing Tasks 1-19 (not placeholders):

- **Schema changes** — the four new columns, their types, the new `ix_legal_document_versions_content_hash` index, and confirmation that `legal_document_versions` still has no RLS/tenant_id (unchanged).
- **Migration name** — the exact generated `<timestamp>_AddLegalDocumentRichContent` migration name from Task 3.
- **API routes added** — all six admin routes (Task 13) and both public routes (Task 14), with method + path.
- **Content storage design** — content_json (jsonb) / content_html (text) / content_text (text) / content_hash (varchar(128), SHA-256 hex of trimmed content_html), and why content_url was kept but not relied upon.
- **Sanitization rules** — the exact allowlist of tags/attributes from `LegalHtmlValidator` (Task 2), and that it is allowlist-first (default-deny), not a blacklist.
- **Publish immutability behavior** — draft-only edits (Task 8), the two-phase archive-then-publish transaction (Task 9) and why (the non-deferrable partial unique index), archive-only-changes-status (Task 10).
- **How pending-login users read documents before accepting** — the Phase-1 anonymous public-read simplification (Task 14) plus `content_endpoint`/`content_hash` now present in `PendingLegalDocumentDto` (Task 15).
- **How new versions force re-acceptance** — publishing archives the prior published row, so `GetCurrentRequiredVersionsAsync`/`LegalAcceptanceChecker` immediately start reporting the new version as pending for anyone who only accepted the old one.
- **Verification results** — actual pass/fail counts from Task 19 Steps 1-3, and the Task 18 integration outcome (ran and passed, or skipped with the Docker-unavailable reason).
- **Skipped checks with exact reason** — at minimum, the integration suite if Docker wasn't available; anything else genuinely skipped, with why.
- **Suggested Postman requests** (not created, per Global Constraints) — list under "Developer Platform": Create/Update/Publish/Archive/List/Get Legal Document Version; under "Organization": Get Current Legal Documents, Get Legal Document By Version, Complete Pending Legal Acceptance.

- [ ] **Step 2: Confirm the report file exists and is non-empty**

Run: `dir LEGAL_DOCUMENT_RICH_CONTENT_MANAGEMENT_REPORT.md` (from the `HRMS-Backend-v1` root)
Expected: file present, non-zero size.

Do not commit — leave the report as an uncommitted new file along with every other change from this plan.
