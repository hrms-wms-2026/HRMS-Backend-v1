using System.Text.RegularExpressions;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Storage.File.Entities;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the Phase 1 file storage foundation rules:
/// - file_records and file_upload_reservations entities are tenant-owned,
/// - the migration creates only the two inventory-approved tables with exact
///   inventory columns,
/// - IFileStorageService/IUploadPurposePolicy implementations live in
///   Infrastructure/Services, IObjectStorageAdapter has no AWS SDK types in
///   its signature (Application stays SDK-free),
/// - no Application code bypasses IFileStorageService by using the file
///   repositories directly,
/// - no Cloudflare R2 credential field names or the R2 endpoint domain appear
///   in appsettings*.json.
/// </summary>
public class FileStorageArchitectureTests
{
    [Fact]
    public void FileRecord_IsTenantOwned_ForIsolationFilters()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(FileRecord)));
    }

    [Fact]
    public void FileUploadReservation_IsTenantOwned_ForIsolationFilters()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(FileUploadReservation)));
    }

    [Fact]
    public void FileStorageService_ImplementationLivesInInfrastructureServices()
    {
        var implementations = typeof(ONEVO.Infrastructure.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IFileStorageService).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(implementations);
        Assert.All(implementations, t =>
            Assert.StartsWith("ONEVO.Infrastructure.Services.Storage.File", t.Namespace ?? string.Empty));
    }

    [Fact]
    public void ObjectStorageAdapterInterface_HasNoAwsSdkTypesInItsSignatures()
    {
        var methods = typeof(IObjectStorageAdapter).GetMethods();

        foreach (var method in methods)
        {
            var returnTypeNamespace = method.ReturnType.Namespace ?? string.Empty;
            Assert.DoesNotContain("Amazon", returnTypeNamespace, StringComparison.OrdinalIgnoreCase);

            foreach (var parameter in method.GetParameters())
            {
                var parameterNamespace = parameter.ParameterType.Namespace ?? string.Empty;
                Assert.DoesNotContain("Amazon", parameterNamespace, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ControllersAndApplicationHandlers_DoNotDependOnStorageAdaptersOrConcreteStorageServices()
    {
        var candidateTypes = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("ONEVO.Api.Controllers", StringComparison.Ordinal) == true)
            .Concat(typeof(ONEVO.Application.DependencyInjection).Assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal)));

        // Match concrete types only. IFileStorageService is the correct Application
        // dependency and must NOT be treated as the concrete FileStorageService.
        var forbiddenNames = new[]
        {
            nameof(IObjectStorageAdapter),
            "CloudflareR2ObjectStorageAdapter",
            "FileStorageService"
        };

        var offenders = candidateTypes
            .SelectMany(t => GetDirectTypeReferences(t)
                .Where(reference => forbiddenNames.Any(name => IsExactTypeNameMatch(reference, name)))
                .Select(reference => $"{t.FullName} -> {reference}"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsExactTypeNameMatch(string fullTypeName, string simpleName)
    {
        // "IFileStorageService" must not match forbidden "FileStorageService".
        var leaf = fullTypeName.Split('.', '+')[^1];
        return leaf.Equals(simpleName, StringComparison.Ordinal);
    }

    [Fact]
    public void FileStorageResponseDtos_DoNotExposeRawObjectKeysOrCredentialFields()
    {
        var dtoTypes = new[] { typeof(FileRecordDto), typeof(FileUploadReservationDto) };
        // StorageKey is intentionally present on FileRecordDto so feature handlers can
        // persist object-key metadata after IFileStorageService.UploadAsync completes.
        // Credential/secret material and provider config must still never appear.
        var forbiddenFragments = new[]
        {
            "Secret", "AccessKey", "Credential", "AccountId", "Bucket", "Endpoint"
        };

        var offenders = dtoTypes
            .SelectMany(t => t.GetProperties()
                .Where(p => forbiddenFragments.Any(fragment =>
                    p.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoApplicationType_BypassesFileStorageService_ByUsingFileRepositoriesDirectly()
    {
        // The two file repositories are internal building blocks of
        // FileStorageService only. Every future upload feature handler must
        // consume IFileStorageService, never file_records/file_upload_reservations
        // repositories directly.
        var repositoryTypeNames = new[]
        {
            "ONEVO.Application.Features.Storage.File.RepositoryInterfaces.IFileRecordRepository",
            "ONEVO.Application.Features.Storage.File.RepositoryInterfaces.IFileUploadReservationRepository"
        };

        var offenders = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(t => t.Namespace?.StartsWith("ONEVO.Application.Features.", StringComparison.Ordinal) == true)
            .Where(t => t.Namespace?.StartsWith("ONEVO.Application.Features.Storage.File", StringComparison.Ordinal) != true)
            .SelectMany(t => GetDirectTypeReferences(t)
                .Where(reference => repositoryTypeNames.Any(reference.Contains))
                .Select(reference => $"{t.FullName} -> {reference}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Migration_CreatesOnly_FileRecordsAndFileUploadReservations()
    {
        var source = ReadFileStorageMigrationSource();

        var createdTables = Regex.Matches(source, "CreateTable\\s*\\(\\s*name:\\s*\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "file_records", "file_upload_reservations" }, createdTables);
    }

    [Fact]
    public void FileRecords_HasExactlyTheInventoryColumns()
    {
        var source = ReadFileStorageMigrationSource();
        var columns = ExtractColumnsForTable(source, "file_records");

        var expected = new[]
        {
            "checksum_sha256",
            "content_type",
            "created_at",
            "deleted_at",
            "detected_content_type",
            "file_size_bytes",
            "id",
            "original_file_name",
            "safe_file_name",
            "scan_completed_at",
            "scan_provider",
            "scan_result_code",
            "status",
            "storage_deleted_at",
            "storage_key",
            "tenant_id",
            "updated_at",
            "uploaded_by_user_id"
        };

        Assert.Equal(expected, columns.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void FileUploadReservations_HasExactlyTheInventoryColumns()
    {
        var source = ReadFileStorageMigrationSource();
        var columns = ExtractColumnsForTable(source, "file_upload_reservations");

        var expected = new[]
        {
            "completed_file_record_id",
            "created_at",
            "expires_at",
            "id",
            "reserved_by_user_id",
            "reserved_bytes",
            "status",
            "tenant_id",
            "updated_at"
        };

        Assert.Equal(expected, columns.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void FileStorageTables_HaveEnabledAndForcedTenantRlsPolicies()
    {
        var migrationsDir = FindMigrationsDirectory();
        var policyFile = Directory.GetFiles(migrationsDir, "*AddFileStorageRlsPolicies.cs")
            .Single(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var source = File.ReadAllText(policyFile);

        foreach (var table in new[] { "file_records", "file_upload_reservations" })
        {
            Assert.Contains($"\"{table}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("ALTER TABLE {table} ENABLE ROW LEVEL SECURITY", source, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE {table} FORCE ROW LEVEL SECURITY", source, StringComparison.Ordinal);
        Assert.Contains("CREATE POLICY tenant_isolation ON {table}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCloudflareR2CredentialFieldNames_AppearInAppsettingsFiles()
    {
        var apiProjectDirectory = FindApiProjectDirectory();
        var appsettingsFiles = Directory.GetFiles(apiProjectDirectory, "appsettings*.json", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(appsettingsFiles);

        foreach (var file in appsettingsFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("secretAccessKey", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessKeyId", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cloudflarestorage.com", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<string> ExtractColumnsForTable(string migrationSource, string tableName)
    {
        // Isolate the CreateTable(...) block for the given table before
        // extracting its columns, so file_records columns are never mixed with
        // file_upload_reservations columns from the same migration file.
        var tableStart = migrationSource.IndexOf($"name: \"{tableName}\"", StringComparison.Ordinal);
        Assert.True(tableStart >= 0, $"migration does not create table '{tableName}'");

        var constraintsIndex = migrationSource.IndexOf("constraints:", tableStart, StringComparison.Ordinal);
        Assert.True(constraintsIndex > tableStart, $"could not find constraints block for '{tableName}'");

        var columnsBlock = migrationSource.Substring(tableStart, constraintsIndex - tableStart);

        return Regex.Matches(columnsBlock, "(?<name>\\w+) = table\\.Column<")
            .Select(m => m.Groups["name"].Value)
            .ToList();
    }

    private static string ReadFileStorageMigrationSource()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*AddFileStorageTables.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(migrationFiles);
        return File.ReadAllText(migrationFiles[0]);
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }

    private static string FindApiProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/ONEVO.Api above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> GetDirectTypeReferences(Type type)
    {
        foreach (var field in type.GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic))
        {
            yield return field.FieldType.FullName ?? field.FieldType.Name;
        }

        foreach (var constructor in type.GetConstructors(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
        }

        foreach (var method in type.GetMethods(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType.FullName ?? method.ReturnType.Name;

            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
        }
    }
}
