using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.Commands.UploadLeaveDocument;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class UploadLeaveDocumentCommandHandlerTests
{
    [Fact]
    public async Task Handle_Forbidden_WhenNotAuthenticated()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var handler = new UploadLeaveDocumentCommandHandler(new Mock<IFileStorageService>().Object, currentUser.Object);
        using var stream = new MemoryStream();
        var result = await handler.Handle(new UploadLeaveDocumentCommand("note.pdf", "application/pdf", stream), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UploadsWithLeaveSupportingDocumentPurpose()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(userId);
        currentUser.Setup(c => c.HasPermission("leave:read-own")).Returns(true);

        var stored = new FileRecordDto(Guid.NewGuid(), tenantId, "key", "note.pdf", "note.pdf", "application/pdf", 12, "abc", "ready", DateTimeOffset.UtcNow);
        var files = new Mock<IFileStorageService>();
        files.Setup(f => f.UploadAsync(tenantId, userId, "note.pdf", "application/pdf", UploadPurposeCatalog.LeaveSupportingDocument, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(stored));

        var handler = new UploadLeaveDocumentCommandHandler(files.Object, currentUser.Object);
        using var stream = new MemoryStream([1, 2, 3]);
        var result = await handler.Handle(new UploadLeaveDocumentCommand("note.pdf", "application/pdf", stream), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stored.Id, result.Value!.Id);
    }
}
