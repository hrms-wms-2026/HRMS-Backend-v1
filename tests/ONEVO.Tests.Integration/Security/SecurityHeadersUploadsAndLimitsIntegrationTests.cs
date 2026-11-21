using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ONEVO.Tests.Integration.OrgStructure.LegalEntity;
using ONEVO.Tests.Integration.Support;
using SkiaSharp;
using Xunit;

namespace ONEVO.Tests.Integration.Security;

/// <summary>
/// Task 23 (security headers, upload checks and request limits), through the real API pipeline:
/// - the header scan: every expected header on a normal endpoint, the health endpoint and a file download;
/// - a 2 MB JSON body to a normal endpoint gets 413, while uploads keep their larger limits;
/// - an .exe renamed to .png is rejected;
/// - an uploaded photo comes back without its GPS EXIF.
/// Runs in the "Test" environment over https, so HSTS is on, as in any non-Development deployment.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class SecurityHeadersUploadsAndLimitsIntegrationTests : IClassFixture<LegalEntitiesIntegrationTestsFixture>
{
    private const string Hsts = "max-age=31536000; includeSubDomains";
    private readonly LegalEntitiesIntegrationTestsFixture _fixture;

    public SecurityHeadersUploadsAndLimitsIntegrationTests(LegalEntitiesIntegrationTestsFixture fixture) => _fixture = fixture;

    // ── Header scan ──────────────────────────────────────────────────────────

    [Fact]
    public async Task HeaderScan_NormalEndpoint_HasEveryHeader()
    {
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantA.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantAPrimaryLegalEntityId}/general-settings",
            body: null, cookie: _fixture.TenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertBaselineHeaders(response, expectedCorp: "same-origin");
        Header(response, "Content-Security-Policy").Should().Be("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task HeaderScan_HealthEndpoint_HasEveryHeader()
    {
        // A tenant host needs a session for every route, /health included; the headers are the point here.
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantA.Host, "/health", body: null,
            cookie: _fixture.TenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertBaselineHeaders(response, expectedCorp: "same-origin");
    }

    [Fact]
    public async Task HeaderScan_FileDownload_IsServedSafely()
    {
        var fileId = await UploadAndLinkLogoAsync(
            TestImages.Encode(SKEncodedImageFormat.Png, 16, 16), "logo.png", "image/png", "HDRSC1");

        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantA.Host,
            $"/api/v1/files/{fileId}", body: null, cookie: _fixture.TenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // same-site: the web app shows logos with <img src> from the API, on another port locally.
        AssertBaselineHeaders(response, expectedCorp: "same-site");
        Header(response, "Content-Security-Policy").Should().Contain("sandbox");
        // A re-encoded PNG may be shown inline; every other type is an attachment.
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
    }

    // ── Body limit ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoMegabyteJsonBody_ToANormalEndpoint_Gets413()
    {
        var json = JsonSerializer.Serialize(new { fileId = Guid.NewGuid(), padding = new string('a', 2 * 1024 * 1024) });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _fixture.SendMultipartAsync(HttpMethod.Put, _fixture.TenantA.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantAPrimaryLegalEntityId}/logo", content,
            cookie: _fixture.TenantA.SessionCookie, csrfToken: _fixture.TenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task TwoMegabyteJsonBody_Anonymous_Gets413BeforeAnythingReadsIt()
    {
        var json = JsonSerializer.Serialize(new { email = "a@b.test", password = new string('a', 2 * 1024 * 1024) });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _fixture.SendMultipartAsync(HttpMethod.Post, "localhost", "/api/v1/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task UploadEndpoint_KeepsItsLargerLimit()
    {
        var bigPng = TestImages.NoisyPng(720, 720);
        bigPng.Length.Should().BeGreaterThan(1024 * 1024, "the upload must be over the 1 MB default to prove anything");

        var response = await UploadAsync(bigPng, "big-logo.png", "image/png", "company_logo");

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    // ── Upload checks ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExeRenamedToPng_IsRejected()
    {
        // A Windows executable header ("MZ"), sent with a .png name and an image/png Content-Type.
        var exe = new byte[512];
        exe[0] = 0x4D;
        exe[1] = 0x5A;

        var response = await UploadAsync(exe, "holiday.png", "image/png", "company_logo");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadedPhoto_ComesBackWithoutItsGpsExif()
    {
        var photo = TestImages.JpegWithGpsExif(64, 48);
        TestImages.Contains(photo, TestImages.ExifHeader).Should().BeTrue();

        var fileId = await UploadAndLinkLogoAsync(photo, "photo.jpg", "image/jpeg", "GPSEX1");
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantA.Host,
            $"/api/v1/files/{fileId}", body: null, cookie: _fixture.TenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await response.Content.ReadAsByteArrayAsync();
        TestImages.Contains(stored, TestImages.ExifHeader).Should().BeFalse("GPS EXIF must be stripped on upload");
        using var codec = SKCodec.Create(SKData.CreateCopy(stored));
        codec.Should().NotBeNull();
        codec!.EncodedFormat.Should().Be(SKEncodedImageFormat.Jpeg);
        codec.Info.Width.Should().Be(64);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertBaselineHeaders(HttpResponseMessage response, string expectedCorp)
    {
        Header(response, "Strict-Transport-Security").Should().Be(Hsts);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Content-Security-Policy").Should().NotBeNullOrEmpty();
        Header(response, "Permissions-Policy").Should().Contain("camera=()").And.Contain("geolocation=()");
        Header(response, "Cross-Origin-Opener-Policy").Should().Be("same-origin");
        Header(response, "Cross-Origin-Resource-Policy").Should().Be(expectedCorp);
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            return string.Join(", ", values);
        return response.Content.Headers.TryGetValues(name, out var contentValues)
            ? string.Join(", ", contentValues)
            : null;
    }

    private async Task<HttpResponseMessage> UploadAsync(byte[] bytes, string fileName, string contentType, string purpose)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(purpose), "purpose");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);

        return await _fixture.SendMultipartAsync(HttpMethod.Post, _fixture.TenantA.Host, "/api/v1/files", content,
            cookie: _fixture.TenantA.SessionCookie, csrfToken: _fixture.TenantA.CsrfHeader);
    }

    private async Task<Guid> UploadAndLinkLogoAsync(byte[] bytes, string fileName, string contentType, string code)
    {
        var upload = await UploadAsync(bytes, fileName, contentType, "company_logo");
        var uploadBody = await upload.Content.ReadAsStringAsync();
        upload.StatusCode.Should().Be(HttpStatusCode.Created, uploadBody);
        var fileId = JsonDocument.Parse(uploadBody).RootElement.GetProperty("fileId").GetGuid();

        var company = await _fixture.CreateCompanyAsync(_fixture.TenantA, $"Task23 {code} Co", code, $"REG-{code}");
        var link = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", new { fileId },
            cookie: _fixture.TenantA.SessionCookie, csrfToken: _fixture.TenantA.CsrfHeader);
        link.StatusCode.Should().Be(HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
        return fileId;
    }
}
