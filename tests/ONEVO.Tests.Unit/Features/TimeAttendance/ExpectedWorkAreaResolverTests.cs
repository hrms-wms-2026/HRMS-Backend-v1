using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public class ExpectedWorkAreaResolverTests
{
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IWorkModeRepository> _workModes = new();
    private readonly Mock<IWorkAreaChangeRequestRepository> _changeRequests = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    private ExpectedWorkAreaResolver CreateResolver()
    {
        _clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        return new ExpectedWorkAreaResolver(_clock.Object, _workModes.Object, _changeRequests.Object);
    }

    [Fact]
    public async Task ResolveAsync_NoApprovedRequest_ReturnsPermanentWorkMode()
    {
        var workModeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, WorkModeId = workModeId
        };
        var legalEntity = new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, Timezone = "UTC" };
        _changeRequests.Setup(x => x.GetApprovedForDateAsync(
                _tenantId, _legalEntityId, employee.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        _workModes.Setup(x => x.GetByIdAsync(_tenantId, workModeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkMode
            {
                Id = workModeId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Hybrid",
                IsActive = true, SelfRegistersLocation = true, AllowsDailyLocationChoice = false
            });

        var result = await CreateResolver().ResolveAsync(employee, legalEntity, DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.True(result.IsSuccess);
        Assert.Equal(workModeId, result.Value!.WorkModeId);
        Assert.Equal("Hybrid", result.Value.WorkModeName);
        Assert.Equal(ExpectedWorkAreaResolver.SourceActiveWorkMode, result.Value.Source);
        Assert.True(result.Value.SelfRegistersLocation);
        Assert.False(result.Value.AllowsDailyLocationChoice);
    }

    [Fact]
    public async Task ResolveAsync_ApprovedRequestExists_ReturnsRequestedWorkModeNotPermanentOne()
    {
        var permanentWorkModeId = Guid.NewGuid();
        var requestedWorkModeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, WorkModeId = permanentWorkModeId
        };
        var legalEntity = new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, Timezone = "UTC" };
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        _changeRequests.Setup(x => x.GetApprovedForDateAsync(_tenantId, _legalEntityId, employee.Id, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkAreaChangeRequest
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employee.Id, LegalEntityId = _legalEntityId,
                Date = date, RequestedWorkModeId = requestedWorkModeId, RequestedWorkModeName = "Field Crew",
                Status = WorkAreaChangeRequest.StatusApproved
            });
        _workModes.Setup(x => x.GetByIdAsync(_tenantId, requestedWorkModeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkMode
            {
                Id = requestedWorkModeId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Field Crew",
                IsActive = true, SelfRegistersLocation = true, AllowsDailyLocationChoice = false
            });

        var result = await CreateResolver().ResolveAsync(employee, legalEntity, date);

        Assert.True(result.IsSuccess);
        Assert.Equal(requestedWorkModeId, result.Value!.WorkModeId);
        Assert.Equal("Field Crew", result.Value.WorkModeName);
        Assert.Equal(ExpectedWorkAreaResolver.SourceApprovedRequest, result.Value.Source);
        // The requested mode's OWN location-behavior flags must be read live from WorkMode - the
        // change request only cached Id/Name at approval time, and an admin could have re-toggled
        // the mode's flags since.
        Assert.True(result.Value.SelfRegistersLocation);
        Assert.False(result.Value.AllowsDailyLocationChoice);
    }

    [Fact]
    public async Task ResolveAsync_ApprovedRequestTargetsDeletedWorkMode_FallsBackToOfficeChecked()
    {
        var permanentWorkModeId = Guid.NewGuid();
        var requestedWorkModeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, WorkModeId = permanentWorkModeId
        };
        var legalEntity = new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, Timezone = "UTC" };
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        _changeRequests.Setup(x => x.GetApprovedForDateAsync(_tenantId, _legalEntityId, employee.Id, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkAreaChangeRequest
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employee.Id, LegalEntityId = _legalEntityId,
                Date = date, RequestedWorkModeId = requestedWorkModeId, RequestedWorkModeName = "Deleted Mode",
                Status = WorkAreaChangeRequest.StatusApproved
            });
        _workModes.Setup(x => x.GetByIdAsync(_tenantId, requestedWorkModeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkMode?)null);

        var result = await CreateResolver().ResolveAsync(employee, legalEntity, date);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SelfRegistersLocation);
        Assert.False(result.Value.AllowsDailyLocationChoice);
    }

    [Fact]
    public async Task ResolveAsync_EmployeeHasNoWorkModeAssigned_ReturnsConflict()
    {
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, WorkModeId = null };
        var legalEntity = new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, Timezone = "UTC" };
        _changeRequests.Setup(x => x.GetApprovedForDateAsync(
                _tenantId, _legalEntityId, employee.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);

        var result = await CreateResolver().ResolveAsync(employee, legalEntity, DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
