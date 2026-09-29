using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Commands.UploadFile;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public sealed class UploadFileCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private UploadFileCommandHandler CreateHandler()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new UploadFileCommandHandler(_fileStorage.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_UnsupportedPurpose_ReturnsBadRequestWithoutUploading()
    {
        using var stream = new MemoryStream();

        var result = await CreateHandler().Handle(
            new UploadFileCommand("unsupported", "a.png", "image/png", stream), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _fileStorage.Verify(x => x.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SupportedPurpose_DelegatesToStorage()
    {
        using var stream = new MemoryStream();
        var record = new FileRecordDto(
            Guid.NewGuid(), TenantId, "key", "a.png", "a.png", "image/png", 100, "sha",
            "active", DateTimeOffset.UtcNow, UserId, null);
        _fileStorage.Setup(x => x.UploadAsync(
                TenantId, UserId, "a.png", "image/png", "employee_avatar", stream,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(record));

        var result = await CreateHandler().Handle(
            new UploadFileCommand("employee_avatar", "a.png", "image/png", stream), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(record.Id);
    }
}
