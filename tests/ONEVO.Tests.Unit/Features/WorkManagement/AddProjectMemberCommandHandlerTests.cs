using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AddProjectMember;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AddProjectMemberCommandHandlerTests
{
    [Fact]
    public void AddProjectMemberCommand_ReusesAddObjectiveMemberOutcomeResponse()
    {
        var projectId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        IRequest<Result<AddObjectiveMemberOutcomeResponse>> command = new AddProjectMemberCommand(projectId, employeeId);

        Assert.Equal(projectId, ((AddProjectMemberCommand)command).ProjectId);
        Assert.Equal(employeeId, ((AddProjectMemberCommand)command).EmployeeId);
    }
}
