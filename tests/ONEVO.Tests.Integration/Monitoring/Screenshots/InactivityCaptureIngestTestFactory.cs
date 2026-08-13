using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Monitoring.Screenshots;

public sealed class InactivityCaptureIngestTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InactivityCaptureIngestTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = "inactivity-test-jwt-secret-min-32-chars!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                ["TrayApp:Jwt:Secret"] = "inactivity-test-tray-jwt-secret-min-32chars!",
                ["TrayApp:Jwt:Issuer"] = "onevo-tray",
                ["TrayApp:Jwt:Audience"] = "onevo-tray-app",
                ["Tenancy:RootDomain"] = "localhost",
                ["Urls:AppBaseUrl"] = "https://localhost",
                ["Encryption:MasterKey"] = "inactivity-test-master-key-32-chars-xxxx!",
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "Inactivity Test Super Admin"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.UseNpgsql(_connectionString)
                       .UseSnakeCaseNamingConvention();
            });

            services.RemoveAll<ITotpService>();
            services.AddSingleton<ITotpService, AlwaysValidTotpService>();
            services.RemoveAll<IGoogleIdTokenValidator>();
            services.AddSingleton<IGoogleIdTokenValidator, EmailAsGoogleTokenValidator>();
            services.RemoveAll<IPlatformOAuthAppResolver>();
            services.AddSingleton<IPlatformOAuthAppResolver, TestPlatformOAuthAppResolver>();

            services.RemoveAll<IFileStorageService>();
            services.AddScoped<IFileStorageService, PersistingFileStorageService>();
        });
    }

    private sealed class AlwaysValidTotpService : ITotpService
    {
        public bool Verify(string base32Secret, string code) => code == "123456";
    }

    private sealed class EmailAsGoogleTokenValidator : IGoogleIdTokenValidator
    {
        public Task<GoogleIdTokenPayload?> ValidateAsync(
            string idToken,
            string expectedAudience,
            CancellationToken ct = default)
        {
            return Task.FromResult<GoogleIdTokenPayload?>(
                new GoogleIdTokenPayload(idToken, idToken, true, "Google Test User"));
        }
    }

    private sealed class TestPlatformOAuthAppResolver : IPlatformOAuthAppResolver
    {
        public Task<ResolvedPlatformOAuthApp?> GetActiveAppForProviderAsync(
            string provider,
            CancellationToken ct = default)
        {
            return Task.FromResult<ResolvedPlatformOAuthApp?>(
                new ResolvedPlatformOAuthApp(
                    provider,
                    "test-google-client",
                    "https://accounts.google.com/o/oauth2/v2/auth",
                    "https://oauth2.googleapis.com/token",
                    []));
        }

        public Task<ResolvedPlatformOAuthAppCredential?> GetActiveCredentialForProviderAsync(
            string provider,
            CancellationToken ct = default)
        {
            return Task.FromResult<ResolvedPlatformOAuthAppCredential?>(null);
        }
    }

    internal sealed class PersistingFileStorageService : IFileStorageService
    {
        private readonly ApplicationDbContext _db;

        public PersistingFileStorageService(ApplicationDbContext db) => _db = db;

        public Task<Result<FileUploadReservationDto>> BeginReservationAsync(
            Guid tenantId,
            Guid userId,
            string originalFileName,
            string contentType,
            long fileSizeBytes,
            string purpose,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result<FileUploadReservationDto>.Success(
                new FileUploadReservationDto(
                    Guid.NewGuid(),
                    tenantId,
                    fileSizeBytes,
                    "active",
                    DateTimeOffset.UtcNow.AddMinutes(15),
                    DateTimeOffset.UtcNow)));
        }

        public Task<Result<FileRecordDto>> CompleteUploadAsync(
            Guid tenantId,
            Guid reservationId,
            string purpose,
            string originalFileName,
            string contentType,
            string checksumSha256,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result<FileRecordDto>.Success(
                new FileRecordDto(
                    Guid.NewGuid(),
                    tenantId,
                    $"tenants/{tenantId}/monitoring/screenshots/{Guid.NewGuid()}/{originalFileName}",
                    originalFileName,
                    originalFileName,
                    contentType,
                    0,
                    checksumSha256,
                    FileRecordStatus.Available,
                    DateTimeOffset.UtcNow)));
        }

        public Task<Result> CancelReservationAsync(
            Guid tenantId,
            Guid reservationId,
            string reason,
            CancellationToken ct = default)
            => Task.FromResult(Result.Success());

        public Task<Result<FileRecordDto>> UploadAsync(
            Guid tenantId,
            Guid userId,
            string originalFileName,
            string contentType,
            string purpose,
            Stream content,
            CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            var size = content.CanSeek ? content.Length : 0;
            var storageKey = $"tenants/{tenantId}/monitoring/screenshots/{id}/{originalFileName}";

            _db.FileRecords.Add(new FileRecord
            {
                Id = id,
                TenantId = tenantId,
                StorageKey = storageKey,
                OriginalFileName = originalFileName,
                SafeFileName = originalFileName,
                ContentType = contentType,
                FileSizeBytes = size,
                ChecksumSha256 = "test-checksum",
                UploadedByUserId = userId,
                Status = FileRecordStatus.Available,
                CreatedAt = DateTimeOffset.UtcNow
            });

            return Task.FromResult(Result<FileRecordDto>.Success(
                new FileRecordDto(
                    id,
                    tenantId,
                    storageKey,
                    originalFileName,
                    originalFileName,
                    contentType,
                    size,
                    "test-checksum",
                    FileRecordStatus.Available,
                    DateTimeOffset.UtcNow)));
        }

        public Task<Result<string>> GetSignedUrlAsync(
            Guid tenantId,
            Guid fileRecordId,
            TimeSpan expiry,
            CancellationToken ct = default)
            => Task.FromResult(Result<string>.Success("https://signed.example/test"));

        public Task<Result<FileStreamDto>> OpenReadAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default)
            => Task.FromResult(Result<FileStreamDto>.NotFound("File not found."));
    }
}
