using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Employees;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ResendEmployeeInvitation;
using ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;
using ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List employees visible to the caller. Tenant-scoped, paginated, and filtered
    /// by the caller's management-coverage-derived visibility scope unless they hold
    /// org:manage.</summary>
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] Guid? legalEntityId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListEmployeesQuery(search, departmentId, legalEntityId, page, pageSize), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single employee by ID. Returns 404 if the employee does not exist in the
    /// caller's tenant, 403 if it exists but is outside the caller's visibility scope.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Full section-by-section detail read for one employee. Payroll is included only
    /// when the caller holds employees:read:sensitive - omitted (not a 403) otherwise.</summary>
    [HttpGet("{id:guid}/detail")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeDetailQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Resend an employee onboarding invitation. Only succeeds when the employee's
    /// current invitation has expired without being accepted - see
    /// ResendEmployeeInvitationCommandHandler for the exact guard.</summary>
    [HttpPost("{id:guid}/resend-invitation")]
    [RequirePermission("invitations:manage")]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ResendEmployeeInvitationCommand(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Revoke an employee's current onboarding invitation and free its reserved seat.
    /// Unlike resend, this works on a still-pending invitation, not only an expired one.</summary>
    [HttpPost("{id:guid}/revoke-invitation")]
    [RequirePermission("invitations:manage")]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RevokeEmployeeInvitationCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Composite read of the caller's own profile: personal info, job info (read-only),
    /// emergency contacts, dependents, masked payroll, and security status. Self-service only -
    /// no permission code required, matches profile-management.md's "authenticated self-service".</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyProfileQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update the caller's own Personal Information. Optimistic concurrency: Version must
    /// match the xmin token returned by GetMyProfile, or this returns 409.</summary>
    [HttpPut("me/personal-information")]
    public async Task<IActionResult> UpdateMyPersonalInformation(
        [FromBody] UpdatePersonalInformationRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdatePersonalInformationCommand(
                request.FirstName, request.LastName, request.Phone, request.DateOfBirth,
                request.Gender, request.NationalityId, request.DisplayTimezone,
                request.Addresses.Select(a => new UpdateAddressInput(a.AddressType, a.AddressJson, a.IsPrimary)).ToList(),
                request.Version),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Upload/replace the caller's own avatar photo.</summary>
    [HttpPut("me/avatar")]
    public async Task<IActionResult> SetMyAvatar(IFormFile file, CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(
            new SetMyAvatarCommand(file.FileName, file.ContentType, stream), ct);

        return result.IsSuccess
            ? Ok(new { avatarFileId = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Add an emergency contact for the caller's own profile.</summary>
    [HttpPost("me/emergency-contacts")]
    public async Task<IActionResult> AddMyEmergencyContact([FromBody] UpsertEmergencyContactRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new AddEmergencyContactCommand(request.Name, request.Relationship, request.Phone, request.Email, request.IsPrimary), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMyProfile), null, new { id = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update one of the caller's own emergency contacts.</summary>
    [HttpPut("me/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> UpdateMyEmergencyContact(
        Guid contactId, [FromBody] UpsertEmergencyContactRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateEmergencyContactCommand(contactId, request.Name, request.Relationship, request.Phone, request.Email, request.IsPrimary), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Remove one of the caller's own emergency contacts.</summary>
    [HttpDelete("me/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> DeleteMyEmergencyContact(Guid contactId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteEmergencyContactCommand(contactId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Add a dependent for the caller's own profile.</summary>
    [HttpPost("me/dependents")]
    public async Task<IActionResult> AddMyDependent([FromBody] UpsertDependentRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new AddDependentCommand(request.Name, request.Relationship, request.DateOfBirth, request.IsEmergencyContact, request.Phone), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMyProfile), null, new { id = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update one of the caller's own dependents.</summary>
    [HttpPut("me/dependents/{dependentId:guid}")]
    public async Task<IActionResult> UpdateMyDependent(
        Guid dependentId, [FromBody] UpsertDependentRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateDependentCommand(dependentId, request.Name, request.Relationship, request.DateOfBirth, request.IsEmergencyContact, request.Phone), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Remove one of the caller's own dependents.</summary>
    [HttpDelete("me/dependents/{dependentId:guid}")]
    public async Task<IActionResult> DeleteMyDependent(Guid dependentId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteDependentCommand(dependentId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get the caller's own masked payroll/bank details.</summary>
    [HttpGet("me/payroll")]
    public async Task<IActionResult> GetMyPayroll(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyPayrollQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update the caller's own bank details. Requires employees:write even for the
    /// caller's own record - bank-detail edits are HR-mediated to prevent unauthorized
    /// payroll-redirection (see design spec §6).</summary>
    [HttpPut("me/payroll")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> UpdateMyPayroll([FromBody] UpdateBankDetailsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateBankDetailsCommand(
                request.BankName, request.BranchName, request.AccountHolderName,
                request.AccountNumber, request.AccountType, request.RoutingNumber),
            ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
