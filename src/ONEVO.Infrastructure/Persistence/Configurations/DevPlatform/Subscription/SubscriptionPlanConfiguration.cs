using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Subscription;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Tier).HasMaxLength(20).IsRequired();
        builder.Property(p => p.FeatureLimitsJson).HasColumnType("jsonb");
        builder.Property(p => p.IncludedModulesJson).HasColumnType("jsonb");
        builder.Property(p => p.CompanySizeRange).HasMaxLength(30).IsRequired();
        builder.Property(p => p.PricingUnit).HasMaxLength(30).IsRequired();
        builder.Property(p => p.CalculatedMonthlyPrice).HasColumnType("decimal(10,2)");
        builder.Property(p => p.CalculatedAnnualPrice).HasColumnType("decimal(10,2)");
        builder.Property(p => p.OverrideMonthlyPrice).HasColumnType("decimal(10,2)");
        builder.Property(p => p.OverrideAnnualPrice).HasColumnType("decimal(10,2)");
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();

        builder.HasData(
            new SubscriptionPlan
            {
                Id = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000001"),
                Name = "Starter - 51-200",
                Code = "starter_51_200",
                Tier = "starter",
                CompanySizeRange = "51-200",
                IncludedModulesJson = """["auth","configuration","roles","notifications","org","workflow_engine","core_hr","leave","calendar","monitoring","workforce","verification","exceptions","analytics","work_management","chat","chat_ai","integrations"]""",
                CalculatedMonthlyPrice = 7.50m,
                CalculatedAnnualPrice = 75.00m,
                Currency = "USD",
                TrialPeriodDays = 30,
                UnpaidGracePeriodDays = 7,
                IsActive = true,
                CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            }
        );
    }
}
