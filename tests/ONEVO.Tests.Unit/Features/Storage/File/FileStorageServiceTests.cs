using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
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
        FakeUnitOfWork unitOfWork)
    {
        return new FileStorageService(
            reservations,
            fileRecords,
            quota,
            objectStorage,
            new UploadPurposePolicy(),
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

    [Fact]
    public async Task UploadAsync_ObjectStorageFailure_ReleasesReservationAndDoesNotComplete()
    {
        var tenantId = Guid.NewGuid();
        var reservations = new FakeFileUploadReservationRepository();
        var fileRecords = new FakeFileRecordRepository();
        var quota = new FakeStorageQuotaService();
        var objectStorage = new FakeObjectStorageAdapter { ShouldFailPut = true };
        var service = CreateService(reservations, fileRecords, quota, objectStorage, new FakeUnitOfWork());

        var bytes = System.Text.Encoding.UTF8.GetBytes("fake image bytes");
        using var content = new MemoryStream(bytes);

        var result = await service.UploadAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", "employee_avatar", content, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, quota.ReleaseCallCount);
        Assert.Equal(0, quota.CommitCallCount);
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
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake image bytes"));

        var result = await service.UploadAsync(
            tenantId, Guid.NewGuid(), "photo.png", "image/png", "employee_avatar", content, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Single(objectStorage.DeletedObjectKeys);
        Assert.Equal(1, quota.ReleaseCallCount);
        Assert.Equal(0, reservations.AtomicCompletionCount);
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
}
