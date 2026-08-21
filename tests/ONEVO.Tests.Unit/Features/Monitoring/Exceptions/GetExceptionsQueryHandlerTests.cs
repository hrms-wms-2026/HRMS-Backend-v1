using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptions;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Xunit;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class GetExceptionsQueryHandlerTests
{
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private static readonly Guid TenantId = Guid.NewGuid();

    private GetExceptionsQueryHandler BuildSut()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetExceptionsQueryHandler(_exceptions.Object, _tenantContext.Object);
    }

    [Fact]
    public async Task Handle_ReturnsFilteredPagedResults()
    {
        _exceptions.Setup(r => r.GetListTotalCountAsync(TenantId, ExceptionStatus.Open, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _exceptions.Setup(r => r.GetListAsync(TenantId, ExceptionStatus.Open, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DomainException
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Guid.NewGuid(),
                Type = ExceptionType.SustainedLowActivity, Status = ExceptionStatus.Open,
                Title = "Sustained low activity", Description = "desc", DetectedAt = DateTimeOffset.UtcNow
            }]);
        var sut = BuildSut();

        var result = await sut.Handle(new GetExceptionsQuery { Status = ExceptionStatus.Open }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(e => e.Title == "Sustained low activity");
    }
}
