using FluentAssertions;
using Moq;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using PositionAssignmentEntity = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class EditedOnboardingTasksValidatorTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Validate_EmployeeTask_DoesNotRequireAssignedToId()
    {
        var json = "[{\"title\":\"Complete profile\",\"ownerType\":\"employee\",\"dueDate\":\"2026-09-01\",\"isRequired\":true}]";

        var result = await EditedOnboardingTasksValidator.ValidateAsync(
            _tenantId, json, _employees.Object, _assignments.Object, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _employees.Verify(r => r.GetDefaultForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Validate_AnotherPersonTask_RequiresConcreteAssignedToId()
    {
        var json = $"[{{\"title\":\"IT setup\",\"ownerType\":\"custom_user\",\"assigneePositionId\":\"{Guid.NewGuid()}\",\"dueDate\":\"2026-09-01\",\"isRequired\":true}}]";

        var result = await EditedOnboardingTasksValidator.ValidateAsync(
            _tenantId, json, _employees.Object, _assignments.Object, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Validate_AnotherPersonTask_WithActiveAssignee_Succeeds()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var json = $"[{{\"title\":\"IT setup\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{userId}\",\"assigneePositionId\":\"{positionId}\",\"dueDate\":\"2026-09-01\",\"isRequired\":true}}]";
        _employees.Setup(r => r.GetDefaultForUserAsync(_tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = employeeId, UserId = userId, EmploymentStatusId = EmploymentStatusIds.Active });
        _assignments.Setup(r => r.GetActivePrimaryAsync(_tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAssignmentEntity { EmployeeId = employeeId, PositionId = positionId, AssignmentStatus = PositionAssignmentStatus.Active });

        var result = await EditedOnboardingTasksValidator.ValidateAsync(
            _tenantId, json, _employees.Object, _assignments.Object, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_InactiveAssignee_Returns400()
    {
        var userId = Guid.NewGuid();
        var json = $"[{{\"title\":\"IT setup\",\"ownerType\":\"custom_user\",\"assignedToId\":\"{userId}\",\"dueDate\":\"2026-09-01\",\"isRequired\":true}}]";
        _employees.Setup(r => r.GetDefaultForUserAsync(_tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { UserId = userId, EmploymentStatusId = EmploymentStatusIds.Terminated });

        var result = await EditedOnboardingTasksValidator.ValidateAsync(
            _tenantId, json, _employees.Object, _assignments.Object, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
