using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Compliance;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Legal;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Compliance;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Legal;

public class LegalAcceptanceInfrastructureTests
{
    [Fact]
    public void LegalDocumentVersion_HasExact13ApprovedColumns()
    {
        var properties = typeof(LegalDocumentVersion).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        properties.Length.Should().Be(13);
        
        var names = properties.Select(p => p.Name).ToList();
        names.Should().Contain(nameof(LegalDocumentVersion.Id));
        names.Should().Contain(nameof(LegalDocumentVersion.DocumentType));
        names.Should().Contain(nameof(LegalDocumentVersion.Version));
        names.Should().Contain(nameof(LegalDocumentVersion.Title));
        names.Should().Contain(nameof(LegalDocumentVersion.ContentUrl));
        names.Should().Contain(nameof(LegalDocumentVersion.IsRequired));
        names.Should().Contain(nameof(LegalDocumentVersion.BlockScope));
        names.Should().Contain(nameof(LegalDocumentVersion.Status));
        names.Should().Contain(nameof(LegalDocumentVersion.PublishedById));
        names.Should().Contain(nameof(LegalDocumentVersion.PublishedAt));
        names.Should().Contain(nameof(LegalDocumentVersion.PublishReason));
        names.Should().Contain(nameof(LegalDocumentVersion.CreatedAt));
        names.Should().Contain(nameof(LegalDocumentVersion.UpdatedAt));
    }

    [Fact]
    public void LegalAcceptanceRecord_HasExact11ApprovedColumns()
    {
        var properties = typeof(LegalAcceptanceRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        properties.Length.Should().Be(11);
        
        var names = properties.Select(p => p.Name).ToList();
        names.Should().Contain(nameof(LegalAcceptanceRecord.Id));
        names.Should().Contain(nameof(LegalAcceptanceRecord.TenantId));
        names.Should().Contain(nameof(LegalAcceptanceRecord.UserId));
        names.Should().Contain(nameof(LegalAcceptanceRecord.DocumentType));
        names.Should().Contain(nameof(LegalAcceptanceRecord.DocumentVersion));
        names.Should().Contain(nameof(LegalAcceptanceRecord.Decision));
        names.Should().Contain(nameof(LegalAcceptanceRecord.Required));
        names.Should().Contain(nameof(LegalAcceptanceRecord.DecidedAt));
        names.Should().Contain(nameof(LegalAcceptanceRecord.IpAddress));
        names.Should().Contain(nameof(LegalAcceptanceRecord.UserAgent));
        names.Should().Contain(nameof(LegalAcceptanceRecord.Source));
    }

    [Fact]
    public async Task LegalAcceptanceChecker_ReturnsNotConfigured_WhenNoPublishedVersionsExist()
    {
        var mockVersionRepo = new TestVersionRepository([]);
        var mockAcceptanceRepo = new TestAcceptanceRepository([]);
        var fixedClock = new TestDateTimeProvider(DateTimeOffset.UtcNow);

        var checker = new LegalAcceptanceChecker(mockVersionRepo, mockAcceptanceRepo, fixedClock);

        var result = await checker.CheckAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Status.Should().Be(LegalAcceptanceStatus.NotConfigured);
        result.IsComplete.Should().BeFalse();
        result.ErrorCode.Should().Be("MissingRequiredLegalVersions");
        result.PendingDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task LegalAcceptanceChecker_UsesIDateTimeProvider_AndFiltersFuturePublishedRows()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var fixedClock = new TestDateTimeProvider(now);

        var pastVersion = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentType = "terms",
            Version = "1.0",
            Title = "Terms v1.0",
            IsRequired = true,
            BlockScope = "dashboard",
            Status = "published",
            PublishedAt = now.AddHours(-1)
        };

        var futureVersion = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentType = "privacy_notice",
            Version = "2.0",
            Title = "Privacy v2.0",
            IsRequired = true,
            BlockScope = "dashboard",
            Status = "published",
            PublishedAt = now.AddHours(2)
        };

        var mockVersionRepo = new TestVersionRepository([pastVersion, futureVersion]);
        var mockAcceptanceRepo = new TestAcceptanceRepository([]);

        var checker = new LegalAcceptanceChecker(mockVersionRepo, mockAcceptanceRepo, fixedClock);

        var result = await checker.CheckAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Status.Should().Be(LegalAcceptanceStatus.Pending);
        result.IsComplete.Should().BeFalse();
        result.PendingDocuments.Should().HaveCount(1);
        result.PendingDocuments[0].DocumentType.Should().Be("terms");
    }

    private class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.Date);
        public TestDateTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;
    }

    private class TestVersionRepository : ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces.ILegalDocumentVersionRepository
    {
        private readonly List<LegalDocumentVersion> _versions;
        public TestVersionRepository(List<LegalDocumentVersion> versions) => _versions = versions;

        public Task<IReadOnlyList<LegalDocumentVersion>> GetCurrentRequiredVersionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LegalDocumentVersion>>(_versions);

        public Task<LegalDocumentVersion?> GetByDocumentTypeAndVersionAsync(string documentType, string version, CancellationToken ct = default)
            => Task.FromResult(_versions.FirstOrDefault(v => v.DocumentType == documentType && v.Version == version));

        public Task AddAsync(LegalDocumentVersion entity, CancellationToken ct = default)
        {
            _versions.Add(entity);
            return Task.CompletedTask;
        }
    }

    private class TestAcceptanceRepository : ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces.ILegalAcceptanceRepository
    {
        private readonly List<LegalAcceptanceRecord> _records;
        public TestAcceptanceRepository(List<LegalAcceptanceRecord> records) => _records = records;

        public Task<IReadOnlyList<LegalAcceptanceRecord>> GetUserAcceptancesAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LegalAcceptanceRecord>>(_records.Where(r => r.TenantId == tenantId && r.UserId == userId).ToList());

        public Task<LegalAcceptanceRecord?> GetUserAcceptanceForDocumentAsync(Guid tenantId, Guid userId, string documentType, string version, CancellationToken ct = default)
            => Task.FromResult(_records.FirstOrDefault(r => r.TenantId == tenantId && r.UserId == userId && r.DocumentType == documentType && r.DocumentVersion == version));

        public Task AddAsync(LegalAcceptanceRecord record, CancellationToken ct = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
