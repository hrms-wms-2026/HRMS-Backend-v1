namespace ONEVO.Infrastructure.Services.SharedPlatform;

/// <summary><c>Caching:ReferenceData</c> (improvements task 22, DEC-105).</summary>
public sealed class ReferenceDataCacheOptions
{
    public const string SectionName = "Caching:ReferenceData";

    /// <summary>False sends every reference-data read to the database (for debugging).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Absolute lifetime of an entry. Saves drop entries sooner; this bounds a missed invalidation.</summary>
    public int LifetimeMinutes { get; set; } = 5;
}
