using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskPendingUploadCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private (CreateTaskPendingUploadCommandHandler Handler, Mock<IFileStorageService> FileStorage) Build()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var fileStorage = new Mock<IFileStorageService>();
        var handler = new CreateTaskPendingUploadCommandHandler(currentUser.Object, fileStorage.Object);
        return (handler, fileStorage);
    }

    [Fact]
    public async Task Handle_UnsupportedPurpose_ReturnsBadRequest()
    {
        var (handler, _) = Build();
        using var stream = new MemoryStream(new byte[] { 1 });

        var result = await handler.Handle(
            new CreateTaskPendingUploadCommand("not_a_real_purpose", "a.png", "image/png", stream), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SupportedPurpose_UploadsAndReturnsFileRecord()
    {
        var (handler, fileStorage) = Build();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var fileId = Guid.NewGuid();
        fileStorage.Setup(x => x.UploadAsync(
                TenantId, UserId, "a.png", "image/png", UploadPurposeCatalog.TaskAttachment, stream, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                fileId, TenantId, "key", "a.png", "a.png", "image/png", 3, new string('a', 64), "available", DateTimeOffset.UtcNow, Guid.NewGuid(), null)));

        var result = await handler.Handle(
            new CreateTaskPendingUploadCommand(UploadPurposeCatalog.TaskAttachment, "a.png", "image/png", stream), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, result.Value!.Id);
    }
}
