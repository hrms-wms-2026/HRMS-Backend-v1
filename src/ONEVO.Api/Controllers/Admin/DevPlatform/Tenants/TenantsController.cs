using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ConfirmTenantProvisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;
using ONEVO.Application.Features.Auth.Invite.Commands.InviteTenantAdmin;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateTenant;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetProvisioningSummary;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetTenantById;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenants;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ValidateTenant;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

/// <summary>
/// Developer Platform tenant lifecycle and provisioning APIs. All endpoints
/// require an admin cookie session (AdminScheme) plus the documented platform
/// permission code (platform.tenants.read / platform.tenants.manage), resolved
/// from the database via platform_user_roles -> platform_role_permissions.
/// </summary>
[ApiController]
[Route("admin/v1/tenants")]
public class TenantsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TenantsController(IMediator mediator) => _mediator = mediator;

    /// <summary>List tenants for the developer console.</summary>
    [HttpGet]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsRead)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery(Name = "plan_code")] string? planCode,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListTenantsQuery(search, status, planCode, page, pageSize),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Slug / company-name / registration uniqueness checks for the wizard.</summary>
    [HttpGet("validate")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> Validate(
        [FromQuery] string? slug,
        [FromQuery(Name = "company_name")] string? companyName,
        [FromQuery(Name = "email_domain")] string? emailDomain,
        [FromQuery(Name = "registration_number")] string? registrationNumber,
        [FromQuery] string? country,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ValidateTenantQuery(slug, companyName, emailDomain, registrationNumber, country),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Create a tenant draft (status = provisioning). Supports the Idempotency-Key
    /// header: retries with the same key and body replay the original response.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    [Idempotent]
    public async Task<IActionResult> Create(
        [FromBody] CreateTenantRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateTenantCommand(
                Name: request.Name,
                Slug: request.Slug,
                IndustryProfile: request.IndustryProfile,
                CompanySizeRange: request.CompanySizeRange,
                LegalEntityName: request.LegalEntityName,
                RegistrationNumber: request.RegistrationNumber,
                Country: request.Country,
                Timezone: request.Timezone,
                Currency: request.Currency,
                Subscription: new SubscriptionInfo(
                    request.Subscription.PlanId,
                    request.Subscription.BillingCycle,
                    request.Subscription.CommercialModel,
                    request.Subscription.TrialPeriodDays,
                    request.Subscription.UnpaidGracePeriodDays),
                OwnerInvite: request.OwnerInvite),
            ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.TenantId }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get tenant detail (visible regardless of provisioning status).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Edit tenant draft fields. Only allowed while status = provisioning.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTenantRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateTenantCommand(
                TenantId: id,
                Name: request.Name,
                Slug: request.Slug,
                IndustryProfile: request.IndustryProfile),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Suspend, unsuspend, or activate a tenant. activate is only valid post-provisioning.</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        [FromBody] TenantStatusUpdateRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ChangeTenantStatusCommand(id, request.Action, request.Reason),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Send the tenant owner invitation email and create the user/role/invite records.</summary>
    [HttpPost("{id:guid}/invite-admin")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> InviteAdmin(
        Guid id,
        [FromBody] InviteTenantAdminRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new InviteTenantAdminCommand(
                TenantId: id,
                Email: request.Email,
                FirstName: request.FirstName,
                LastName: request.LastName,
                RoleId: request.RoleId,
                CompletionMethods: request.CompletionMethods,
                AllowGoogleEmailMismatch: request.AllowGoogleEmailMismatch,
                AllowedEmailDomains: request.AllowedEmailDomains),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Aggregated provisioning checklist drawn from tenant_details + owner_invite + Member-2 readers.</summary>
    [HttpGet("{id:guid}/provisioning-summary")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsRead)]
    public async Task<IActionResult> GetProvisioningSummary(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProvisioningSummaryQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>
    /// Confirm/activate the tenant. Returns 204 on success, 422 with the
    /// provisioning summary when blocking sections are still incomplete.
    /// </summary>
    [HttpPatch("{id:guid}/provision/confirm")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> ConfirmProvisioning(
        Guid id,
        [FromBody] ProvisionConfirmRequest request,
        CancellationToken ct)
    {
        if (!request.Confirm)
            return Problem(
                "confirm must be true to activate the tenant.",
                statusCode: StatusCodes.Status400BadRequest);

        var result = await _mediator.Send(new ConfirmTenantProvisioningCommand(id), ct);

        if (result.IsSuccess)
            return NoContent();

        if (result.StatusCode == StatusCodes.Status422UnprocessableEntity)
        {
            var summary = await _mediator.Send(new GetProvisioningSummaryQuery(id), ct);
            return summary.IsSuccess
                ? UnprocessableEntity(summary.Value)
                : Problem(summary.Error, statusCode: summary.StatusCode ?? 500);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
