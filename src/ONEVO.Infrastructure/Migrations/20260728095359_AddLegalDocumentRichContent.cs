using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Self-contained by design: migrations must reproduce the exact schema/data change they
    /// recorded at authoring time forever, independent of later edits to application-layer code.
    /// Referencing the Application layer's bootstrap-content and hashing helpers here would let a
    /// future rename or wording tweak in that layer silently change what this migration backfills
    /// when replayed from scratch - so the bootstrap literals and the hash algorithm are duplicated
    /// locally instead (see the constants and ComputeContentHash below, which must stay
    /// byte-identical to their Application-layer counterparts).
    /// </summary>
    /// <inheritdoc />
    public partial class AddLegalDocumentRichContent : Migration
    {
        private const string TermsHtml =
            "<h1>ONEVO Terms and Conditions</h1><p>These are the ONEVO Terms and Conditions (Bootstrap Dev). By using ONEVO, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.</p>";

        private const string TermsText =
            "ONEVO Terms and Conditions\n\nThese are the ONEVO Terms and Conditions (Bootstrap Dev). By using ONEVO, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.";

        private const string TermsJson =
            "{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":1},\"content\":[{\"type\":\"text\",\"text\":\"ONEVO Terms and Conditions\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"These are the ONEVO Terms and Conditions (Bootstrap Dev). By using ONEVO, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.\"}]}]}";

        private const string PrivacyHtml =
            "<h1>ONEVO Privacy Notice</h1><p>This is the ONEVO Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how ONEVO collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.</p>";

        private const string PrivacyText =
            "ONEVO Privacy Notice\n\nThis is the ONEVO Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how ONEVO collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.";

        private const string PrivacyJson =
            "{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":1},\"content\":[{\"type\":\"text\",\"text\":\"ONEVO Privacy Notice\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"This is the ONEVO Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how ONEVO collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.\"}]}]}";

        private const string GenericFallbackHtml =
            "<p>This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.</p>";

        private const string GenericFallbackText =
            "This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.";

        private const string GenericFallbackJson =
            "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.\"}]}]}";

        /// <inheritdoc />
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

            var termsHash = ComputeContentHash(TermsHtml);
            var privacyHash = ComputeContentHash(PrivacyHtml);
            var genericHash = ComputeContentHash(GenericFallbackHtml);

            migrationBuilder.Sql($@"
                UPDATE legal_document_versions
                SET content_json = {SqlLiteral(TermsJson)}::jsonb,
                    content_html = {SqlLiteral(TermsHtml)},
                    content_text = {SqlLiteral(TermsText)},
                    content_hash = {SqlLiteral(termsHash)}
                WHERE document_type = 'terms' AND version = '1.0';
            ");

            migrationBuilder.Sql($@"
                UPDATE legal_document_versions
                SET content_json = {SqlLiteral(PrivacyJson)}::jsonb,
                    content_html = {SqlLiteral(PrivacyHtml)},
                    content_text = {SqlLiteral(PrivacyText)},
                    content_hash = {SqlLiteral(privacyHash)}
                WHERE document_type = 'privacy_notice' AND version = '1.0';
            ");

            migrationBuilder.Sql($@"
                UPDATE legal_document_versions
                SET content_json = {SqlLiteral(GenericFallbackJson)}::jsonb,
                    content_html = {SqlLiteral(GenericFallbackHtml)},
                    content_text = {SqlLiteral(GenericFallbackText)},
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_legal_document_versions_content_hash",
                table: "legal_document_versions");

            migrationBuilder.DropColumn(
                name: "content_json",
                table: "legal_document_versions");

            migrationBuilder.DropColumn(
                name: "content_html",
                table: "legal_document_versions");

            migrationBuilder.DropColumn(
                name: "content_text",
                table: "legal_document_versions");

            migrationBuilder.DropColumn(
                name: "content_hash",
                table: "legal_document_versions");
        }

        private static string SqlLiteral(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>
        /// Must stay byte-identical to the Application layer's content-hash helper: SHA-256 over
        /// the trimmed HTML, lowercase hex. Duplicated here (not referenced) so this migration can
        /// never drift if that helper is later changed - see the class-level remarks.
        /// </summary>
        private static string ComputeContentHash(string html)
        {
            var normalized = html.Trim();
            var bytes = Encoding.UTF8.GetBytes(normalized);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
