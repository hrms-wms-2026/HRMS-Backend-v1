using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.Storage;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Queries.GetFile;

namespace ONEVO.Tests.Unit.Controllers.Tenant.Storage;

public sealed class FilesControllerTests
{
    [Fact]
    public async Task Get_CacheableAvatar_ReturnsPrivateCacheHeadersAndContentLength()
    {
        var mediator = new Mock<IMediator>();
        var fileId = Guid.NewGuid();
        var etag = $"\"sha256-{new string('a', 64)}\"";
        var content = new MemoryStream(new byte[] { 1, 2, 3 });
        mediator.Setup(x => x.Send(
                It.Is<GetFileQuery>(query => query.FileId == fileId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileDownloadDto>.Success(new FileDownloadDto(
                content, "image/webp", 3, etag, true, false)));
        var controller = CreateController(mediator.Object);

        var result = await controller.Get(fileId, CancellationToken.None);

        Assert.IsType<FileStreamResult>(result);
        Assert.Equal("private, max-age=900", controller.Response.Headers.CacheControl);
        Assert.Equal(etag, controller.Response.Headers.ETag);
        Assert.Equal("Cookie", controller.Response.Headers.Vary);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal(3, controller.Response.ContentLength);
    }

    [Fact]
    public async Task Get_NotModifiedAvatar_Returns304WithoutContent()
    {
        var mediator = new Mock<IMediator>();
        var fileId = Guid.NewGuid();
        var etag = $"\"sha256-{new string('b', 64)}\"";
        mediator.Setup(x => x.Send(
                It.Is<GetFileQuery>(query =>
                    query.FileId == fileId && query.IfNoneMatch == etag),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileDownloadDto>.Success(new FileDownloadDto(
                null, "image/webp", 100, etag, true, true)));
        var controller = CreateController(mediator.Object);
        controller.Request.Headers.IfNoneMatch = etag;

        var result = await controller.Get(fileId, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        Assert.Equal("private, max-age=900", controller.Response.Headers.CacheControl);
        Assert.Equal(etag, controller.Response.Headers.ETag);
        Assert.Equal("Cookie", controller.Response.Headers.Vary);
        Assert.Null(controller.Response.ContentLength);
    }

    private static FilesController CreateController(IMediator mediator)
    {
        return new FilesController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
