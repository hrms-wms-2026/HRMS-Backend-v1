using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ApproveRemoteLocationChange;
using ONEVO.Application.Features.TimeAttendance.Commands.ApproveWorkAreaChange;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceApprovalCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApproveWorkAreaChange_PendingRequest_RecordsReviewerOnly()
    {
        var tenantId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var request = new WorkAreaChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = Guid.NewGuid(),
            Status = "pending",
            Version = 4
        };
        var repository = new Mock<ITimeAttendanceRepository>();
        var clock = CreateClock();
        var uow = CreateUnitOfWork();
        repository.Setup(value => value.GetWorkAreaChangeAsync(
                request.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await new ApproveWorkAreaChangeCommandHandler(
            repository.Object,
            clock.Object,
            uow.Object).Handle(
                new ApproveWorkAreaChangeCommand(
                    request.Id,
                    4,
                    "Approved for today",
                    tenantId,
                    reviewerId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("approved", request.Status);
        Assert.Equal(reviewerId, request.ReviewedById);
        Assert.Equal(Now, request.ReviewedAt);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApproveRemoteChange_ActivatesCandidateAndArchivesOldProfile()
    {
        var tenantId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var current = new EmployeeRemoteWorkProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Status = "active"
        };
        var candidate = new EmployeeRemoteWorkProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Status = "pending_capture"
        };
        var request = new RemoteWorkLocationChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            CurrentProfileId = current.Id,
            NewProfileId = candidate.Id,
            Status = "pending",
            Version = 2
        };
        var repository = new Mock<IVerificationRepository>();
        var clock = CreateClock();
        var uow = CreateUnitOfWork();
        repository.Setup(value => value.GetRemoteChangeRequestAsync(
                request.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        repository.Setup(value => value.GetRemoteProfileAsync(
                current.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        repository.Setup(value => value.GetRemoteProfileAsync(
                candidate.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var result = await new ApproveRemoteLocationChangeCommandHandler(
            repository.Object,
            clock.Object,
            uow.Object).Handle(
                new ApproveRemoteLocationChangeCommand(
                    request.Id,
                    2,
                    null,
                    tenantId,
                    reviewerId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("approved", request.Status);
        Assert.Equal("archived", current.Status);
        Assert.Equal(Now, current.ArchivedAt);
        Assert.Equal("active", candidate.Status);
        Assert.Equal(reviewerId, candidate.ApprovedById);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Mock<IDateTimeProvider> CreateClock()
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(provider => provider.UtcNow).Returns(Now);
        return clock;
    }

    private static Mock<IUnitOfWork> CreateUnitOfWork()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return uow;
    }
}
