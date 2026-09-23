using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ClockInPolicyEntity = ONEVO.Domain.Features.TimeAttendance.Entities.ClockInPolicy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.ClockInPolicies;

public class ClockInPolicyMapperTests
{
    [Fact]
    public void ToResponse_Maps_Core_Fields_And_Sorts_LateDeductionRules()
    {
        var entity = new ClockInPolicyEntity
        {
            Id = Guid.NewGuid(),
            LegalEntityId = Guid.NewGuid(),
            Name = "Policy",
            ScopeType = ClockInPolicyEntity.ScopeFullCompany,
            EffectiveFrom = new DateOnly(2026, 8, 21),
            NotificationRecipientResolver = ClockInPolicyEntity.NotificationManagementCoverageOwner,
            CreatedById = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LateDeductionRules =
            [
                new ClockInLateDeductionRule
                {
                    Id = Guid.NewGuid(),
                    LateArrivalMinute = 30,
                    Multiplier = 1,
                    TimeOffTypeId = Guid.NewGuid(),
                    IsActive = true
                },
                new ClockInLateDeductionRule
                {
                    Id = Guid.NewGuid(),
                    LateArrivalMinute = 15,
                    Multiplier = 0,
                    TimeOffTypeId = Guid.NewGuid(),
                    IsActive = true
                }
            ]
        };

        var response = ClockInPolicyMapper.ToResponse(entity);

        Assert.Equal(entity.Id, response.Id);
        Assert.Equal(entity.Name, response.Name);
        Assert.Equal(15, response.LateDeductionRules[0].LateArrivalMinute);
        Assert.Equal(30, response.LateDeductionRules[1].LateArrivalMinute);
    }
}
