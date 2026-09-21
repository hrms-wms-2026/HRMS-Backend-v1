using Moq;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.CheckTrayUpdate;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.GetLatestTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.TrayReleases;

public sealed class TrayReleaseHandlersTests
{
    private static CreateTrayReleaseRequest ValidRequest(string version = "1.2.0") => new(
        Version: version, Channel: "beta",
        DownloadUrl: "https://dl.example.com/onevo.msix",
        Sha256: new string('a', 64), FileSizeBytes: 100,
        Publisher: "CN=ONEVO", MinimumWindowsVersion: "10.0.19041.0",
        MinSupportedVersion: null, ReleaseNotes: "notes", IsActive: false);

    private static TrayAppRelease Row(string version, string? min = null, bool active = true) => new()
    {
        Id = Guid.NewGuid(), Version = version, Channel = "stable",
        DownloadUrl = "https://dl.example.com/x.msix", Sha256 = new string('b', 64),
        FileSizeBytes = 5, Publisher = "CN=ONEVO", MinimumWindowsVersion = "10.0.19041.0",
        MinSupportedVersion = min, IsActive = active, Source = "admin"
    };

    // ---- Create ----

    [Fact]
    public async Task Create_Succeeds_AndSaves()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByChannelAndVersionAsync("beta", "1.2.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);
        var handler = new CreateTrayReleaseCommandHandler(repo.Object);

        var result = await handler.Handle(
            new CreateTrayReleaseCommand(ValidRequest(), "admin", Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.Is<TrayAppRelease>(x => x.Version == "1.2.0" && x.Source == "admin"), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.0")]
    public async Task Create_RejectsBadVersion(string version)
    {
        var handler = new CreateTrayReleaseCommandHandler(new Mock<ITrayAppReleaseRepository>().Object);
        var result = await handler.Handle(
            new CreateTrayReleaseCommand(ValidRequest(version), "admin", null), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsBadChannel_BadSha_AndNonHttpsUrl()
    {
        var handler = new CreateTrayReleaseCommandHandler(new Mock<ITrayAppReleaseRepository>().Object);

        var badChannel = ValidRequest() with { Channel = "nightly" };
        var badSha = ValidRequest() with { Sha256 = "xyz" };
        var badUrl = ValidRequest() with { DownloadUrl = "http://insecure.example.com/a.msix" };

        foreach (var req in new[] { badChannel, badSha, badUrl })
        {
            var r = await handler.Handle(new CreateTrayReleaseCommand(req, "admin", null), CancellationToken.None);
            Assert.Equal(400, r.StatusCode);
        }
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenChannelVersionExists()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByChannelAndVersionAsync("beta", "1.2.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("1.2.0"));
        var handler = new CreateTrayReleaseCommandHandler(repo.Object);

        var result = await handler.Handle(
            new CreateTrayReleaseCommand(ValidRequest(), "ci", null), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Create_NormalisesSha256ToLowercase()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        TrayAppRelease? saved = null;
        repo.Setup(r => r.AddAsync(It.IsAny<TrayAppRelease>(), It.IsAny<CancellationToken>()))
            .Callback<TrayAppRelease, CancellationToken>((r, _) => saved = r)
            .Returns(Task.CompletedTask);
        var handler = new CreateTrayReleaseCommandHandler(repo.Object);

        await handler.Handle(new CreateTrayReleaseCommand(
            ValidRequest() with { Sha256 = new string('A', 64) }, "admin", null), CancellationToken.None);

        Assert.Equal(new string('a', 64), saved!.Sha256);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_TogglesActive_AndSaves()
    {
        var row = Row("1.0.0", active: false);
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var handler = new UpdateTrayReleaseCommandHandler(repo.Object);

        var result = await handler.Handle(
            new UpdateTrayReleaseCommand(row.Id, new UpdateTrayReleaseRequest(null, null, null, true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(row.IsActive);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_NotFound()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);
        var result = await new UpdateTrayReleaseCommandHandler(repo.Object).Handle(
            new UpdateTrayReleaseCommand(Guid.NewGuid(), new UpdateTrayReleaseRequest(null, null, null, true)),
            CancellationToken.None);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Update_Promote_ReturnsConflict_WhenTargetChannelAlreadyHasThatVersion()
    {
        var beta = Row("1.0.0"); beta.Channel = "beta";
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(beta.Id, It.IsAny<CancellationToken>())).ReturnsAsync(beta);
        repo.Setup(r => r.GetByChannelAndVersionAsync("stable", "1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("1.0.0"));

        var result = await new UpdateTrayReleaseCommandHandler(repo.Object).Handle(
            new UpdateTrayReleaseCommand(beta.Id, new UpdateTrayReleaseRequest("stable", null, null, null)),
            CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Update_RejectsInvalidMinSupportedVersion()
    {
        var row = Row("1.0.0");
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var result = await new UpdateTrayReleaseCommandHandler(repo.Object).Handle(
            new UpdateTrayReleaseCommand(row.Id, new UpdateTrayReleaseRequest(null, "1.x", null, null)),
            CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    // ---- GetLatest ----

    [Fact]
    public async Task GetLatest_ReturnsSnakeCaseInfo_ForActiveRelease()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("1.4.0"));

        var result = await new GetLatestTrayReleaseQueryHandler(repo.Object)
            .Handle(new GetLatestTrayReleaseQuery("stable"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("1.4.0", result.Value!.Version);
    }

    [Fact]
    public async Task GetLatest_NotFound_WhenNoActiveRelease()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);

        var result = await new GetLatestTrayReleaseQueryHandler(repo.Object)
            .Handle(new GetLatestTrayReleaseQuery("stable"), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task GetLatest_RejectsUnknownChannel()
    {
        var result = await new GetLatestTrayReleaseQueryHandler(new Mock<ITrayAppReleaseRepository>().Object)
            .Handle(new GetLatestTrayReleaseQuery("nightly"), CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }

    // ---- CheckUpdate ----

    [Theory]
    [InlineData("1.0.0", "1.2.0", null,    true,  false)] // newer exists, not forced
    [InlineData("1.2.0", "1.2.0", null,    false, false)] // already latest
    [InlineData("2.0.0", "1.2.0", null,    false, false)] // ahead of latest (dev build)
    [InlineData("1.0.0", "1.2.0", "1.1.0", true,  true )] // below min supported => mandatory
    [InlineData("1.1.0", "1.2.0", "1.1.0", true,  false)] // at min => optional
    public async Task Check_ComputesUpdateAvailableAndMandatory(
        string current, string latest, string? min, bool expectedAvailable, bool expectedMandatory)
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(latest, min));

        var result = await new CheckTrayUpdateQueryHandler(repo.Object)
            .Handle(new CheckTrayUpdateQuery(current, "stable"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedAvailable, result.Value!.UpdateAvailable);
        Assert.Equal(expectedMandatory, result.Value.Mandatory);
        Assert.Equal(expectedAvailable, result.Value.Latest is not null);
    }

    [Fact]
    public async Task Check_NoActiveRelease_MeansNoUpdate_NotAnError()
    {
        var repo = new Mock<ITrayAppReleaseRepository>();
        repo.Setup(r => r.GetLatestActiveAsync("stable", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayAppRelease?)null);

        var result = await new CheckTrayUpdateQueryHandler(repo.Object)
            .Handle(new CheckTrayUpdateQuery("1.0.0", "stable"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.UpdateAvailable);
    }

    [Fact]
    public async Task Check_RejectsUnparseableCurrentVersion()
    {
        var result = await new CheckTrayUpdateQueryHandler(new Mock<ITrayAppReleaseRepository>().Object)
            .Handle(new CheckTrayUpdateQuery("garbage", "stable"), CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }
}
