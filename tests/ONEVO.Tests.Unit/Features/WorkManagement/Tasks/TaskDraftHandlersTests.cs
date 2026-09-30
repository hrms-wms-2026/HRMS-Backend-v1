using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.TaskDrafts;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.TaskDrafts;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class TaskDraftHandlersTests
{
    private readonly Mock<ITaskDraftRepository> _repo = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _draftId = Guid.NewGuid();

    public TaskDraftHandlersTests()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
    }

    private SaveTaskDraftCommandHandler SaveSut() => new(_repo.Object, _uow, _currentUser.Object);

    private DeleteTaskDraftCommandHandler DeleteSut() =>
        new(_repo.Object, _uow, _currentUser.Object, _mediator.Object, NullLogger<DeleteTaskDraftCommandHandler>.Instance);

    private ListMyTaskDraftsQueryHandler ListSut() => new(_repo.Object, _currentUser.Object);

    private GetTaskDraftQueryHandler GetSut() => new(_repo.Object, _currentUser.Object);

    [Fact]
    public async Task Save_New_CreatesOwnedDraft_TrimmedTitle()
    {
        TaskDraft? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TaskDraft>(), It.IsAny<CancellationToken>()))
            .Callback<TaskDraft, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        var r = await SaveSut().Handle(new SaveTaskDraftCommand(null, _projectId, "  Fix login  ", "{\"priority\":\"high\"}"), CancellationToken.None);

        Assert.True(r.IsSuccess);
        Assert.Equal((_userId, _tenantId, "Fix login", _projectId), (added!.OwnerUserId, added.TenantId, added.Title, added.ProjectId));
        Assert.Equal(1, _uow.SaveCallCount);
    }

    [Fact]
    public async Task Save_ExistingNotOwned_IsNotFound()
    {
        _repo.Setup(r => r.GetOwnedAsync(_tenantId, _userId, _draftId, It.IsAny<CancellationToken>())).ReturnsAsync((TaskDraft?)null);
        var r = await SaveSut().Handle(new SaveTaskDraftCommand(_draftId, _projectId, "x", "{}"), CancellationToken.None);
        Assert.Equal(404, r.StatusCode);
    }

    [Fact]
    public async Task Save_Existing_UpdatesFieldsAndTimestamp()
    {
        var existing = new TaskDraft { Id = _draftId, TenantId = _tenantId, OwnerUserId = _userId, ProjectId = _projectId, Title = "old", PayloadJson = "{}" };
        _repo.Setup(r => r.GetOwnedAsync(_tenantId, _userId, _draftId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var r = await SaveSut().Handle(new SaveTaskDraftCommand(_draftId, _projectId, "new", "{\"a\":1}"), CancellationToken.None);

        Assert.True(r.IsSuccess);
        Assert.Equal(("new", "{\"a\":1}"), (existing.Title, existing.PayloadJson));
        Assert.NotNull(existing.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("{not json")]
    public void Validator_RejectsNonObjectPayload(string payload) =>
        Assert.False(new SaveTaskDraftCommandValidator().Validate(new SaveTaskDraftCommand(null, _projectId, "t", payload)).IsValid);

    [Fact]
    public void Validator_RejectsOversizedPayload() =>
        Assert.False(new SaveTaskDraftCommandValidator().Validate(
            new SaveTaskDraftCommand(null, _projectId, "t", "{\"d\":\"" + new string('x', TaskDraftLimits.MaxPayloadLength) + "\"}")).IsValid);

    [Fact]
    public void Validator_AcceptsObjectPayload() =>
        Assert.True(new SaveTaskDraftCommandValidator().Validate(new SaveTaskDraftCommand(null, _projectId, null, "{}")).IsValid);

    [Fact]
    public async Task Delete_RemovesDraft_AndBestEffortDeletesPendingAttachments()
    {
        var f1 = Guid.NewGuid();
        var f2 = Guid.NewGuid();
        var draft = new TaskDraft { Id = _draftId, TenantId = _tenantId, OwnerUserId = _userId, PayloadJson = $"{{\"attachmentFileIds\":[\"{f1}\",\"{f2}\"]}}" };
        _repo.Setup(r => r.GetOwnedAsync(_tenantId, _userId, _draftId, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        _mediator.Setup(m => m.Send(It.Is<DeleteTaskPendingUploadCommand>(c => c.FileId == f1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Conflict("This file is already attached and cannot be deleted as a pending upload."));
        _mediator.Setup(m => m.Send(It.Is<DeleteTaskPendingUploadCommand>(c => c.FileId == f2), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));

        var r = await DeleteSut().Handle(new DeleteTaskDraftCommand(_draftId), CancellationToken.None);

        Assert.True(r.IsSuccess);
        _repo.Verify(x => x.Remove(draft), Times.Once);
        _mediator.Verify(m => m.Send(It.IsAny<DeleteTaskPendingUploadCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Delete_NotOwned_IsNotFound()
    {
        _repo.Setup(r => r.GetOwnedAsync(_tenantId, _userId, _draftId, It.IsAny<CancellationToken>())).ReturnsAsync((TaskDraft?)null);
        Assert.Equal(404, (await DeleteSut().Handle(new DeleteTaskDraftCommand(_draftId), CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task List_ReturnsCallersDraftsAsSummaries()
    {
        _repo.Setup(r => r.ListOwnedAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new TaskDraft { Id = _draftId, ProjectId = _projectId, Title = "t", CreatedAt = DateTimeOffset.UnixEpoch } });

        var r = await ListSut().Handle(new ListMyTaskDraftsQuery(), CancellationToken.None);

        Assert.Equal((_draftId, _projectId, "t", DateTimeOffset.UnixEpoch), (r.Value![0].Id, r.Value[0].ProjectId, r.Value[0].Title, r.Value[0].UpdatedAt));
    }

    [Fact]
    public async Task Get_Owned_ReturnsDraft()
    {
        _repo.Setup(r => r.GetOwnedAsync(_tenantId, _userId, _draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskDraft { Id = _draftId, ProjectId = _projectId, Title = "t", PayloadJson = "{}", CreatedAt = DateTimeOffset.UnixEpoch });

        var r = await GetSut().Handle(new GetTaskDraftQuery(_draftId), CancellationToken.None);

        Assert.True(r.IsSuccess);
        Assert.Equal("{}", r.Value!.PayloadJson);
    }

    [Fact]
    public async Task Get_NotOwned_IsNotFound()
    {
        _repo.Setup(r => r.GetOwnedAsync(_tenantId, _userId, _draftId, It.IsAny<CancellationToken>())).ReturnsAsync((TaskDraft?)null);
        Assert.Equal(404, (await GetSut().Handle(new GetTaskDraftQuery(_draftId), CancellationToken.None)).StatusCode);
    }
}
