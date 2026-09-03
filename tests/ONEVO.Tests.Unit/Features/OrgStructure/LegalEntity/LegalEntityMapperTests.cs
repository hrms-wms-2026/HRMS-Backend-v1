using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public sealed class LegalEntityMapperTests
{
    [Fact]
    public void ToListItemResponse_IncludesCountryCode()
    {
        var entity = new ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity
        {
            Id = Guid.NewGuid(),
            Name = "ONEXSO Lanka",
            CompanyCode = "LK-01",
            CountryCode = "LK",
            IsActive = true,
            IsPrimary = true
        };

        var response = LegalEntityMapper.ToListItemResponse(entity);

        Assert.Equal("LK", response.CountryCode);
    }
}
