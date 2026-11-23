namespace ONEVO.Application.Common.Validation;

/// <summary>
/// Column sizes that many validators share (task 19b). Each value is the EF column size of the table the input lands
/// in, so a validator built on these answers with a 400 what the database write would otherwise fail with a 500.
/// </summary>
public static class InputLimits
{
    /// <summary>file_records.original_file_name, support_ticket_attachments.file_name and
    /// bulk_onboarding_batches.original_file_name are nvarchar(255).</summary>
    public const int FileNameMaxLength = 255;

    /// <summary>file_records.content_type and support_ticket_attachments.content_type are nvarchar(150).</summary>
    public const int ContentTypeMaxLength = 150;

    /// <summary>Every email column is nvarchar(255).</summary>
    public const int EmailMaxLength = 255;

    /// <summary>A short key from the request (a provider, scope, kind or action name) that is only compared with a
    /// known list, never stored as typed. Stored keys use their exact column size instead.</summary>
    public const int KeyMaxLength = 100;
}
