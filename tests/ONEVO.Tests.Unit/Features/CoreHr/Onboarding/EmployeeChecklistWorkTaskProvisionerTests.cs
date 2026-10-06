using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Domain.Lookups;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class EmployeeChecklistWorkTaskProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_CreatesPrivateAssignedTaskAndLinksChecklistItem()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var actor = new EmployeeEntity { Id = Guid.NewGuid(), TenantId = tenantId, UserId = actorUserId, EmploymentStatusId = EmploymentStatusIds.Active };
        var newHire = new EmployeeEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(),
            FirstName = "Asha", LastName = "Perera", EmployeeNumber = "EMP-7",
            EmploymentStatusId = EmploymentStatusIds.Active
        };
        var checklist = new EmployeeChecklistTask
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = newHire.Id,
            AssignedToId = newHire.UserId, TaskTitle = "Prepare laptop", LifecycleType = "onboarding",
            DueDate = new DateOnly(2026, 10, 10), Sequence = 1
        };
        var office = new OfficeProjectContext(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<string, Guid> { ["onboarding"] = Guid.NewGuid(), ["offboarding"] = Guid.NewGuid() });

        var officeProjects = new Mock<IOfficeProjectProvisioner>();
        var employees = new Mock<IEmployeeRepository>();
        var projects = new Mock<IProjectRepository>();
        var workTasks = new Mock<IWorkTaskRepository>();
        var assignments = new Mock<ITaskAssignmentRepository>();
        var members = new Mock<IProjectMemberRepository>();
        var notifications = new Mock<IWorkNotificationEngine>();
        employees.Setup(repository => repository.GetByUserIdAsync(tenantId, actorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor);
        employees.Setup(repository => repository.GetByUserIdsAsync(tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EmployeeEntity>());
        projects.Setup(repository => repository.IncrementAndGetNextTaskNumberAsync(tenantId, office.ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);
        members.Setup(repository => repository.GetTrackedForObjectiveAsync(
                tenantId, office.ProjectId, office.ObjectiveId, newHire.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);

        WorkTask? createdTask = null;
        TaskAssignment? createdAssignment = null;
        workTasks.Setup(repository => repository.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()))
            .Callback<WorkTask, CancellationToken>((task, _) => createdTask = task)
            .Returns(Task.CompletedTask);
        assignments.Setup(repository => repository.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<TaskAssignment, CancellationToken>((assignment, _) => createdAssignment = assignment)
            .Returns(Task.CompletedTask);

        var sut = new EmployeeChecklistWorkTaskProvisioner(
            officeProjects.Object, employees.Object, projects.Object, workTasks.Object,
            assignments.Object, members.Object, notifications.Object);

        var result = await sut.ProvisionAsync(office, newHire, [checklist], actorUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        createdTask.Should().NotBeNull();
        createdTask!.ShortId.Should().Be("OFFICE-12");
        createdTask.TaskKind.Should().Be(WorkTaskKinds.EmployeeChecklist);
        createdTask.VisibilityScope.Should().Be(WorkTaskVisibilityScopes.Assignees);
        createdTask.ProjectId.Should().Be(office.ProjectId);
        createdTask.ObjectiveId.Should().Be(office.ObjectiveId);
        checklist.WorkTaskId.Should().Be(createdTask.Id);
        createdAssignment!.EmployeeId.Should().Be(newHire.Id);
        members.Verify(repository => repository.AddAsync(
            It.Is<ProjectMember>(member => member.EmployeeId == newHire.Id
                && member.MembershipSource == ProjectMembershipSources.System && member.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(engine => engine.NotifyAsync(
            It.Is<WorkNotificationEvent>(notification => notification.TargetId == createdTask.Id
                && notification.RecipientEmployeeIds.Contains(newHire.Id)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
