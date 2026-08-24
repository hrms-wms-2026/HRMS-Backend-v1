using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.Models;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ClockInPolicyEntity = ONEVO.Domain.Features.TimeAttendance.Entities.ClockInPolicy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.ClockInPolicies;

public class ClockInPolicyMapperTests
{
    [Fact]
    public void ApplyWorkAreaRules_Maps_Hybrid_Api_To_Either_Persistence()
    {
        var entity = new ClockInPolicyEntity
        {
            EitherSourceRule = ClockInPolicyEntity.HybridSourceOnsite,
            FieldPhotoRequirement = ClockInPolicyEntity.FieldPhotoOff
        };

        ClockInPolicyMapper.ApplyWorkAreaRules(entity, new WorkAreaRulesInput(
            new WorkAreaSourceRulesInput(true, false, false, false),
            new WorkAreaSourceRulesInput(false, true, true, true),
            new HybridWorkAreaRulesInput(true, true, false, true, true, ClockInPolicyEntity.HybridSourceRemote),
            new FieldWorkAreaRulesInput(false, true, true, ClockInPolicyEntity.FieldPhotoRequired)));

        Assert.True(entity.EitherBiometricEnabled);
        Assert.True(entity.EitherWebEnabled);
        Assert.False(entity.EitherTrayEnabled);
        Assert.True(entity.EitherPhotoRequired);
        Assert.True(entity.EitherLocationCheckRequired);
        Assert.Equal(ClockInPolicyEntity.HybridSourceRemote, entity.EitherSourceRule);
    }

    [Fact]
    public void ToResponse_Exposes_Hybrid_Not_Either()
    {
        var entity = new ClockInPolicyEntity
        {
            Id = Guid.NewGuid(),
            LegalEntityId = Guid.NewGuid(),
            Name = "Policy",
            ScopeType = ClockInPolicyEntity.ScopeFullCompany,
            EffectiveFrom = new DateOnly(2026, 8, 21),
            EitherBiometricEnabled = true,
            EitherWebEnabled = true,
            EitherTrayEnabled = false,
            EitherPhotoRequired = true,
            EitherLocationCheckRequired = true,
            EitherSourceRule = ClockInPolicyEntity.HybridSourceEmployeeChoice,
            FieldPhotoRequirement = ClockInPolicyEntity.FieldPhotoOptional,
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

        Assert.True(response.WorkAreaRules.Hybrid.BiometricEnabled);
        Assert.Equal(ClockInPolicyEntity.HybridSourceEmployeeChoice, response.WorkAreaRules.Hybrid.SourceRule);
        Assert.Equal(15, response.LateDeductionRules[0].LateArrivalMinute);
        Assert.Equal(30, response.LateDeductionRules[1].LateArrivalMinute);
        Assert.DoesNotContain(
            response.WorkAreaRules.GetType().GetProperties().Select(p => p.Name),
            n => n.Contains("Either", StringComparison.Ordinal));
    }
}
