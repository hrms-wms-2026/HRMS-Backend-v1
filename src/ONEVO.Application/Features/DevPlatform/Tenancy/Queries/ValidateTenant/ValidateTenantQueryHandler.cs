using System.Text.RegularExpressions;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ValidateTenant;

public class ValidateTenantQueryHandler
    : IRequestHandler<ValidateTenantQuery, Result<TenantValidationResponseDto>>
{
    private static readonly Regex SlugPattern =
        new("^[a-z0-9](?:[a-z0-9-]{1,48}[a-z0-9])?$", RegexOptions.Compiled);

    private static readonly Regex EmailDomainPattern =
        new("^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?\\.)+[a-zA-Z]{2,}$", RegexOptions.Compiled);

    private readonly ITenantRepository _tenants;

    public ValidateTenantQueryHandler(ITenantRepository tenants) => _tenants = tenants;

    public async Task<Result<TenantValidationResponseDto>> Handle(
        ValidateTenantQuery request,
        CancellationToken ct)
    {
        var conflicts = new List<TenantValidationConflictDto>();
        var warnings = new List<TenantValidationWarningDto>();

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var slug = request.Slug.Trim().ToLowerInvariant();
            if (!SlugPattern.IsMatch(slug))
            {
                conflicts.Add(TenancyMapper.ToConflictDto(
                    "slug",
                    "slug must be 2-50 chars, lowercase a-z/0-9/hyphen, no leading/trailing hyphen."));
            }
            else if (await _tenants.SlugExistsAsync(slug, excludeId: null, ct))
            {
                conflicts.Add(TenancyMapper.ToConflictDto(
                    "slug",
                    $"slug '{slug}' is already taken."));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.EmailDomain))
        {
            var domain = request.EmailDomain.Trim().TrimStart('@').ToLowerInvariant();
            if (!EmailDomainPattern.IsMatch(domain))
            {
                warnings.Add(TenancyMapper.ToWarningDto(
                    "email_domain",
                    "email domain looks malformed; double-check before saving."));
            }
        }

        return Result<TenantValidationResponseDto>.Success(new TenantValidationResponseDto(
            Valid: conflicts.Count == 0,
            Conflicts: conflicts,
            Warnings: warnings));
    }
}
