using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.Queries.ListPositionTemplatePacks;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.PositionTemplatePacks;

public sealed class ListPositionTemplatePacksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IConfigurationTemplateRepository> _templates = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    public ListPositionTemplatePacksQueryHandlerTests()
    {
        _tenantContext.Setup(t => t.TenantId).Returns(TenantId);
    }

    private ListPositionTemplatePacksQueryHandler BuildSut() =>
        new(_templates.Object, _entitlements.Object, _tenantContext.Object);

    private static ConfigurationTemplate ValidTemplate(string templateKey = "small-software-company") =>
        new()
        {
            Id = Guid.NewGuid(),
            TemplateKey = templateKey,
            TemplateType = ConfigurationTemplate.TypePositionTemplate,
            Name = "Small Software Company Positions",
            Description = "Starter leadership and engineering structure.",
            ModuleKeysJson = """["core_hr"]""",
            IsActive = true,
            IsSystem = true,
            PayloadJson =
                """
                {
                  "pack_name": "Small Software Company Positions",
                  "employee_count_range_key": "11-50",
                  "employee_count_min": 11,
                  "employee_count_max": 50,
                  "industry": "software",
                  "positions": [
                    {
                      "position_key": "managing-director",
                      "position_name": "Managing Director",
                      "department_name": "Leadership",
                      "reports_to_position_key": null,
                      "linked_role_template_id": null
                    }
                  ]
                }
                """
        };

    [Fact]
    public async Task Handle_TenantContextMissing_ReturnsForbidden()
    {
        _tenantContext.Setup(t => t.TenantId).Returns(Guid.Empty);

        var sut = BuildSut();
        var result = await sut.Handle(new ListPositionTemplatePacksQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_RequestsOnlyActivePositionTemplateType_FromRepository()
    {
        _templates
            .Setup(t => t.ListAsync(
                ConfigurationTemplate.TypePositionTemplate, true, null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate>());

        var sut = BuildSut();
        var result = await sut.Handle(new ListPositionTemplatePacksQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        _templates.VerifyAll();
    }

    [Fact]
    public async Task Handle_MapsPayload_ToDocumentedResponseShape()
    {
        var template = ValidTemplate();
        _templates
            .Setup(t => t.ListAsync(
                ConfigurationTemplate.TypePositionTemplate, true, null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate> { template });
        _entitlements
            .Setup(e => e.IsModuleEnabledAsync(TenantId, "core_hr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = BuildSut();
        var result = await sut.Handle(new ListPositionTemplatePacksQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);

        var dto = result.Value!.Items[0];
        dto.Id.Should().Be(template.Id);
        dto.TemplateKey.Should().Be("small-software-company");
        dto.Name.Should().Be("Small Software Company Positions");
        dto.Description.Should().Be("Starter leadership and engineering structure.");
        dto.IndustryProfileTag.Should().Be("software");
        dto.EmployeeCountRangeKey.Should().Be("11-50");
        dto.EmployeeCountMin.Should().Be(11);
        dto.EmployeeCountMax.Should().Be(50);
        dto.Positions.Should().ContainSingle();
        dto.Positions[0].PositionKey.Should().Be("managing-director");
        dto.Positions[0].PositionName.Should().Be("Managing Director");
        dto.Positions[0].DepartmentName.Should().Be("Leadership");
        dto.Positions[0].ReportsToPositionKey.Should().BeNull();
        dto.Positions[0].LinkedRoleTemplateId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExcludesTemplate_WhenTenantNotEntitledToRequiredModule()
    {
        var template = ValidTemplate();
        _templates
            .Setup(t => t.ListAsync(
                ConfigurationTemplate.TypePositionTemplate, true, null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate> { template });
        _entitlements
            .Setup(e => e.IsModuleEnabledAsync(TenantId, "core_hr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = BuildSut();
        var result = await sut.Handle(new ListPositionTemplatePacksQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidJsonPayload_ReturnsSafeServerError_WithoutLeakingParseException()
    {
        var template = ValidTemplate();
        template.PayloadJson = "not-json";
        _templates
            .Setup(t => t.ListAsync(
                ConfigurationTemplate.TypePositionTemplate, true, null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate> { template });
        _entitlements
            .Setup(e => e.IsModuleEnabledAsync(TenantId, "core_hr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = BuildSut();
        var result = await sut.Handle(new ListPositionTemplatePacksQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Error.Should().NotContain("JsonException");
        result.Error.Should().NotContain("LineNumber");
        result.Error.Should().NotContain("BytePositionInLine");
        result.Error.Should().NotContain("$.");
    }

    [Fact]
    public async Task Handle_PayloadMissingRequiredPositions_ReturnsSafeServerError()
    {
        var template = ValidTemplate();
        template.PayloadJson =
            """{"pack_name":"Incomplete Pack","employee_count_range_key":"1-10","employee_count_min":1}""";
        _templates
            .Setup(t => t.ListAsync(
                ConfigurationTemplate.TypePositionTemplate, true, null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate> { template });
        _entitlements
            .Setup(e => e.IsModuleEnabledAsync(TenantId, "core_hr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = BuildSut();
        var result = await sut.Handle(new ListPositionTemplatePacksQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Handle_ModuleKeysEmpty_IncludesTemplate_WithoutCallingEntitlementService()
    {
        var template = ValidTemplate();
        template.ModuleKeysJson = "[]";
        _templates
            .Setup(t => t.ListAsync(
                ConfigurationTemplate.TypePositionTemplate, true, null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate> { template });

        var sut = BuildSut();
        var result = await sut.Handle(new ListPositionTemplatePacksQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        _entitlements.Verify(
            e => e.IsModuleEnabledAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
