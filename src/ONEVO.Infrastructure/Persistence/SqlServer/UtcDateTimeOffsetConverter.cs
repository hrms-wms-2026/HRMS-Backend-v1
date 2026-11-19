using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>
/// Keeps every datetimeoffset value at offset zero. The column would otherwise keep whatever offset
/// it was given, and DATEPART, date grouping and what clients read back all depend on UTC.
/// </summary>
public sealed class UtcDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(value => value.ToUniversalTime(), value => value);
