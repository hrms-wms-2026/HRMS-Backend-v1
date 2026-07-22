namespace ONEVO.Infrastructure.Configuration;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public int ReservationTtlMinutes { get; set; } = 30;
}
