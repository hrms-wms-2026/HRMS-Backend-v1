using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Mappers;

internal static class BillingMapper
{
    public static TenantOneTimeChargeDto ToChargeDto(TenantOneTimeCharge charge) =>
        new(charge.Id, charge.TenantId, charge.SetupOptionKey, charge.Description,
            charge.Amount, charge.Currency, charge.Status, charge.ChargedOnce, charge.CreatedAt);
}
