using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.SqlServer;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure;

/// <summary>Task 42: the tables users edit through forms carry the xmin rowversion token, marked for the
/// detached-update fill; projects and personal_calendar_events are left out on purpose.</summary>
public sealed class RowVersionTokenModelTests
{
    private static readonly Microsoft.EntityFrameworkCore.Metadata.IModel Model = BuildModel();

    private static Microsoft.EntityFrameworkCore.Metadata.IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        using var db = new ApplicationDbContext(options,
            new ONEVO.Infrastructure.Persistence.Interceptors.AuditableEntityInterceptor(
                Moq.Mock.Of<ONEVO.Application.Common.ServiceInterfaces.ICurrentUser>(), Moq.Mock.Of<ONEVO.Application.Common.ServiceInterfaces.IDateTimeProvider>()),
            new ONEVO.Infrastructure.Persistence.Interceptors.SoftDeleteInterceptor(Moq.Mock.Of<ONEVO.Application.Common.ServiceInterfaces.IDateTimeProvider>()),
            new ONEVO.Infrastructure.Persistence.Interceptors.DomainEventDispatchInterceptor(Moq.Mock.Of<MediatR.IPublisher>()),
            Moq.Mock.Of<ONEVO.Application.Common.ServiceInterfaces.ITenantContext>());
        return db.Model;
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("objectives")]
    [InlineData("sprints")]
    [InlineData("positions")]
    [InlineData("departments")]
    [InlineData("leave_policies")]
    [InlineData("clock_in_policies")]
    [InlineData("roles")]
    [InlineData("legal_entities")]
    public void Table_has_the_rowversion_concurrency_token(string table)
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == table);
        var token = entity.FindProperty(RowVersionConfiguration.PropertyName);

        token.Should().NotBeNull($"{table} needs the task 42 token");
        token!.IsConcurrencyToken.Should().BeTrue();
        token.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate);
        token.FindAnnotation(RowVersionConfiguration.FillDetachedAnnotation).Should().NotBeNull();
    }

    /// <summary>projects: its task and meeting counters change with raw SQL on every new task. personal_calendar_events:
    /// meeting-link provisioning and calendar sync save the row in the background while users edit it, by design.</summary>
    [Theory]
    [InlineData("projects")]
    [InlineData("personal_calendar_events")]
    public void Tables_written_in_the_background_have_no_token(string table)
    {
        Model.GetEntityTypes().Single(e => e.GetTableName() == table)
            .FindProperty(RowVersionConfiguration.PropertyName).Should().BeNull();
    }
}
