using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.Storage.Quota.Helpers;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Storage.File;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class FileStorageServiceTests
{
    private static FileStorageService CreateService(
        FakeFileUploadReservationRepository reservations,
        FakeFileRecordRepository fileRecords,
        FakeStorageQuotaService quota,
        FakeObjectStorageAdapter objectStorage,
        FakeUnitOfWork unitOfWork,
        IAvatarImageProcessor? avatarImageProcessor = null)
    {
        return new FileStorageService(
            reservations,
            fileRecords,
            quota,
            objectStorage,
            new UploadPurposePolicy(),
            avatarImageProcessor ?? new FakeAvatarImageProcessor(),
            unitOfWork,
            new FakeDateTimeProvider(),
            Options.Create(new FileStorageOptions()),
            NullLogger<FileStorageService>.Instance);
    }

    [Fact]
    public async Task BeginReservationAsync_QuotaExceeded_PreventsReservation()
    {
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService { ReserveShouldSucceed = false };
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.BeginReservationAsync(
            Guid.NewGuid(), Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, quota.ReserveCallCount);
    }

    /// <summary>
    /// Proves the local/dev "logo upload blocked by storage_not_entitled" failure mode
    /// propagates unmodified through FileStorageService: when IStorageQuotaService cannot
    /// resolve a tenant storage allowance (403 storage_not_entitled), BeginReservationAsync
    /// must surface that exact error code and status code, not a generic quota-exceeded
    /// response.
    /// </summary>
    [Fact]
    public async Task BeginReservationAsync_NotEntitled_PropagatesStorageNotEntitledWith403()
    {
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService
        {
            ReserveShouldSucceed = false,
            ReserveFailureError = StorageQuotaErrorCodes.NotEntitled,
            ReserveFailureStatusCode = 403
        };
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.BeginReservationAsync(
            Guid.NewGuid(), Guid.NewGuid(), "logo.png", "image/png", 1024, "company_logo", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(StorageQuotaErrorCodes.NotEntitled, result.Error);
        Assert.Equal(0, reservations.AtomicCompletionCount);
    }

    [Fact]
    public async Task BeginReservationAsync_Success_IncrementsReservedBytes()
    {
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService { ReserveShouldSucceed = true };
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.BeginReservationAsync(
            Guid.NewGuid(), Guid.NewGuid(), "photo.png", "image/png", 2048, "employee_avatar", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, quota.ReserveCallCount);
        Assert.Equal(2048, quota.LastReservedBytes);
    }

    [Fact]
    public async Task CompleteUploadAsync_Success_MovesReservedBytesToUsed()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var begin = await service.BeginReservationAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);

        var complete = await service.CompleteUploadAsync(
            tenantId, begin.Value!.Id, "employee_avatar", "photo.png", "image/png",
            new string('a', 64), CancellationToken.None);

        Assert.True(complete.IsSuccess);
        Assert.Equal(1, reservations.AtomicCompletionCount);
    }

    [Fact]
    public async Task CancelReservationAsync_ReleasesReservedBytes()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var begin = await service.BeginReservationAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);

        var cancel = await service.CancelReservationAsync(tenantId, begin.Value!.Id, "user_cancelled", CancellationToken.None);

        Assert.True(cancel.IsSuccess);
        Assert.Equal(1, quota.ReleaseCallCount);
        Assert.Equal(1024, quota.LastReleasedBytes);
    }

    [Fact]
    public async Task CompleteUploadAsync_DoubleComplete_SecondCallIsRejected()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var begin = await service.BeginReservationAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);

        var firstComplete = await service.CompleteUploadAsync(
            tenantId, begin.Value!.Id, "employee_avatar", "photo.png", "image/png",
            new string('a', 64), CancellationToken.None);
        var secondComplete = await service.CompleteUploadAsync(
            tenantId, begin.Value.Id, "employee_avatar", "photo.png", "image/png",
            new string('a', 64), CancellationToken.None);

        Assert.True(firstComplete.IsSuccess);
        Assert.False(secondComplete.IsSuccess);
        Assert.Equal(409, secondComplete.StatusCode);
    }

    [Fact]
    public async Task CompleteUploadAsync_ExpiredReservation_IsRejectedWithoutCommittingQuota()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var begin = await service.BeginReservationAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);
        var reservation = await reservations.GetByIdAsync(tenantId, begin.Value!.Id, CancellationToken.None);
        reservation!.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        var complete = await service.CompleteUploadAsync(
            tenantId, begin.Value.Id, "employee_avatar", "photo.png", "image/png",
            new string('a', 64), CancellationToken.None);

        Assert.False(complete.IsSuccess);
        Assert.Equal(409, complete.StatusCode);
        Assert.Equal(0, quota.CommitCallCount);
    }

    [Fact]
    public async Task CancelReservationAsync_DoubleCancel_IsIdempotentlySafe()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var begin = await service.BeginReservationAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);

        var firstCancel = await service.CancelReservationAsync(tenantId, begin.Value!.Id, "reason", CancellationToken.None);
        var secondCancel = await service.CancelReservationAsync(tenantId, begin.Value.Id, "reason", CancellationToken.None);

        Assert.True(firstCancel.IsSuccess);
        Assert.True(secondCancel.IsSuccess);
        Assert.Equal(1, quota.ReleaseCallCount);
    }

    [Fact]
    public async Task TenantCannotCompleteOrCancelAnotherTenantsReservation()
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var begin = await service.BeginReservationAsync(
            ownerTenantId, Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);

        var complete = await service.CompleteUploadAsync(
            otherTenantId, begin.Value!.Id, "employee_avatar", "photo.png", "image/png",
            new string('a', 64), CancellationToken.None);
        var cancel = await service.CancelReservationAsync(
            otherTenantId, begin.Value.Id, "cross_tenant_attempt", CancellationToken.None);

        Assert.False(complete.IsSuccess);
        Assert.Equal(404, complete.StatusCode);
        Assert.False(cancel.IsSuccess);
        Assert.Equal(404, cancel.StatusCode);
        Assert.Equal(0, reservations.AtomicCompletionCount);
        Assert.Equal(0, quota.ReleaseCallCount);
    }

    /// <summary>
    /// Reproduces the "logo upload now fails with 502 file_upload_failed after storage
    /// quota entitlement was fixed" scenario: quota reservation succeeds, but
    /// IObjectStorageAdapter.PutObjectAsync throws (e.g. Cloudflare R2 rejects the
    /// upload). UploadAsync must surface exactly file_upload_failed/502 — not a generic
    /// failure — and must cancel the reservation it already made so the bytes are not
    /// left stranded as reserved-but-never-used.
    /// </summary>
    [Fact]
    public async Task UploadAsync_ObjectStorageFailure_ReleasesReservationAndDoesNotComplete()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var fileRecords = new FakeFileRecordRepository();
        var quota = new FakeStorageQuotaService();
        var objectStorage = new FakeObjectStorageAdapter { ShouldFailPut = true };
        var service = CreateService(reservations, fileRecords, quota, objectStorage, new FakeUnitOfWork());

        // Real PNG magic bytes - SEC-13's signature check (UploadPurposePolicy.ValidateSignature)
        // runs before the R2 put this test exercises, so the content must look like a real PNG.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        using var content = new MemoryStream(bytes);

        var result = await service.UploadAsync(
            tenantId, Guid.NewGuid(), "logo.png", "image/png", "company_logo", content, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("file_upload_failed", result.Error);
        Assert.Equal(502, result.StatusCode);
        Assert.Equal(1, quota.ReleaseCallCount);
        Assert.Equal(0, quota.CommitCallCount);
        Assert.Equal(0, reservations.AtomicCompletionCount);
    }

    [Fact]
    public async Task UploadAsync_MetadataCompletionFailure_DeletesObjectAndReleasesReservation()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository { ShouldFailAtomicCompletion = true };
        var quota = new FakeStorageQuotaService();
        var objectStorage = new FakeObjectStorageAdapter();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, objectStorage, new FakeUnitOfWork());
        // FakeAvatarImageProcessor echoes the input bytes back unchanged under a renamed .webp
        // extension - SEC-13's signature check then runs against that .webp name, so the input
        // needs real WEBP magic bytes for this fake passthrough to look valid.
        using var content = new MemoryStream(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 });

        var result = await service.UploadAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", "employee_avatar", content, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Single(objectStorage.DeletedObjectKeys);
        Assert.Equal(1, quota.ReleaseCallCount);
        Assert.Equal(0, reservations.AtomicCompletionCount);
    }

    [Fact]
    public async Task UploadAsync_EmployeeAvatar_StoresNormalizedWebpWithoutChangingOriginalName()
    {
        var tenantId = Guid.NewGuid();
        var avatarProcessor = new FakeAvatarImageProcessor();
        var objectStorage = new FakeObjectStorageAdapter();
        var service = CreateService(
            new FakeFileUploadReservationRepository(),
            new FakeFileRecordRepository(),
            new FakeStorageQuotaService(),
            objectStorage,
            new FakeUnitOfWork(),
            avatarProcessor);
        // Same passthrough-fake reasoning as UploadAsync_MetadataCompletionFailure above: the
        // content must look like a valid WEBP since that's the extension it's checked against.
        using var content = new MemoryStream(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 });

        var result = await service.UploadAsync(
            tenantId,
            Guid.NewGuid(),
            "employee-photo.png",
            "image/png",
            "employee_avatar",
            content,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, avatarProcessor.CallCount);
        Assert.Equal("employee-photo.png", result.Value!.OriginalFileName);
        Assert.Equal("employee-photo.webp", result.Value.SafeFileName);
        Assert.Equal("image/webp", result.Value.ContentType);
        Assert.Equal("image/webp", objectStorage.LastPutContentType);
        Assert.EndsWith("/employee-photo.webp", Assert.Single(objectStorage.PutObjectKeys));
    }

    [Fact]
    public async Task UploadAsync_NonAvatar_DoesNotInvokeAvatarProcessor()
    {
        var avatarProcessor = new FakeAvatarImageProcessor();
        var service = CreateService(
            new FakeFileUploadReservationRepository(),
            new FakeFileRecordRepository(),
            new FakeStorageQuotaService(),
            new FakeObjectStorageAdapter(),
            new FakeUnitOfWork(),
            avatarProcessor);
        // Real PDF magic bytes - SEC-13's signature check runs against the "document.pdf" name.
        using var content = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0, 0, 0, 0 });

        var result = await service.UploadAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "document.pdf",
            "application/pdf",
            "generic_document",
            content,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, avatarProcessor.CallCount);
        Assert.Equal("application/pdf", result.Value!.ContentType);
    }

    [Fact]
    public async Task Errors_NeverContainSecretLookingValues()
    {
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService { ReserveShouldSucceed = false };
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.BeginReservationAsync(
            Guid.NewGuid(), Guid.NewGuid(), "photo.png", "image/png", 1024, "employee_avatar", CancellationToken.None);

        Assert.DoesNotContain("secretAccessKey", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessKeyId", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenReadAsync_FileNotFound_ReturnsNotFound()
    {
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.OpenReadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task OpenReadAsync_Success_ReturnsStreamAndContentType()
    {
        var tenantId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageKey = "tenants/logo/photo.png",
            OriginalFileName = "photo.png",
            SafeFileName = "photo.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(),
            Status = FileRecordStatus.PendingScan,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record, CancellationToken.None);
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, new FakeStorageQuotaService(),
            new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.OpenReadAsync(tenantId, record.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.Value!.ContentType);
        Assert.NotNull(result.Value!.Content);
    }

    [Fact]
    public async Task OpenReadAsync_QuarantinedFile_ReturnsNotFound()
    {
        // SEC-13 (2026-10-12): future-proofing - nothing in production code sets Quarantined
        // today (no scanner exists), so this seeds it directly to prove the read-side gate is
        // wired and ready for when a real scanner starts setting it.
        var tenantId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageKey = "tenants/logo/photo.png",
            OriginalFileName = "photo.png",
            SafeFileName = "photo.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(),
            Status = FileRecordStatus.Quarantined,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record, CancellationToken.None);
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, new FakeStorageQuotaService(),
            new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.OpenReadAsync(tenantId, record.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task OpenReadAsync_PendingScanFile_StillServed_DocumentingTodaysKnownGap()
    {
        // Pins today's known, accepted-as-not-a-launch-blocker gap (SEC-13): a file stuck at
        // PendingScan forever (no scanner flips it) is served exactly like Available. A future
        // change to this must be a deliberate, reviewed diff - not an accidental regression
        // discovered later via this test silently starting to fail for the wrong reason.
        var tenantId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageKey = "tenants/logo/photo.png",
            OriginalFileName = "photo.png",
            SafeFileName = "photo.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(),
            Status = FileRecordStatus.PendingScan,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record, CancellationToken.None);
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, new FakeStorageQuotaService(),
            new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.OpenReadAsync(tenantId, record.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task OpenReadAsync_ObjectStorageFailure_Returns502()
    {
        var tenantId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageKey = "tenants/logo/photo.png",
            OriginalFileName = "photo.png",
            SafeFileName = "photo.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(),
            Status = FileRecordStatus.PendingScan,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record, CancellationToken.None);
        var objectStorage = new FakeObjectStorageAdapter { ShouldFailGet = true };
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, new FakeStorageQuotaService(),
            objectStorage, new FakeUnitOfWork());

        var result = await service.OpenReadAsync(tenantId, record.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_ExistingRecord_MarksDeletedAndReleasesQuota()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, StorageKey = "tenants/x/task-attachments/a.png",
            OriginalFileName = "a.png", SafeFileName = "a.png", ContentType = "image/png",
            FileSizeBytes = 1024, ChecksumSha256 = new string('a', 64), UploadedByUserId = userId,
            Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record);
        var quota = new FakeStorageQuotaService();
        var objectStorage = new FakeObjectStorageAdapter();
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, quota, objectStorage, new FakeUnitOfWork());

        var result = await service.DeleteAsync(tenantId, userId, record.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, quota.ReleaseUsedCallCount);
        Assert.Equal(1024, quota.LastReleasedUsedBytes);
        var reloaded = await fileRecords.GetByIdAsync(tenantId, record.Id);
        Assert.NotNull(reloaded!.DeletedAt);
        Assert.NotNull(reloaded.StorageDeletedAt);
    }

    [Fact]
    public async Task DeleteAsync_UnknownRecord_ReturnsNotFound()
    {
        var service = CreateService(
            new FakeFileUploadReservationRepository(), new FakeFileRecordRepository(),
            new FakeStorageQuotaService(), new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeleted_IsIdempotentSuccess()
    {
        var tenantId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, StorageKey = "k", OriginalFileName = "a.png",
            SafeFileName = "a.png", ContentType = "image/png", FileSizeBytes = 100,
            ChecksumSha256 = new string('a', 64), UploadedByUserId = Guid.NewGuid(),
            Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow, StorageDeletedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record);
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.DeleteAsync(tenantId, record.UploadedByUserId, record.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, quota.ReleaseUsedCallCount);
    }
}
