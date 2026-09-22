using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyAvatar;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class GetMyAvatarQueryHandlerTests
{
    private readonly Mock<CommonEmployeeRepo> _employees = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.NewGuid();

    private void AuthenticateCurrentUser()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecordForCurrentUser_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        _employees.Setup(r => r.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);
        var sut = new GetMyAvatarQueryHandler(_employees.Object, _fileStorage.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMyAvatarQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _fileStorage.Verify(f => f.OpenReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoAvatarSet_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        _employees.Setup(r => r.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, AvatarFileId = null });
        var sut = new GetMyAvatarQueryHandler(_employees.Object, _fileStorage.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMyAvatarQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_AvatarSet_DelegatesToFileStorageWithTheStoredFileId()
    {
        AuthenticateCurrentUser();
        var avatarFileId = Guid.NewGuid();
        _employees.Setup(r => r.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, AvatarFileId = avatarFileId });
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, avatarFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));
        var sut = new GetMyAvatarQueryHandler(_employees.Object, _fileStorage.Object, _currentUser.Object);

        var result = await sut.Handle(new GetMyAvatarQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
        _fileStorage.Verify(f => f.OpenReadAsync(TenantId, avatarFileId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
