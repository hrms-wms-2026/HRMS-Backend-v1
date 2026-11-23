namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b test data shared by the per-module validator tests.</summary>
internal static class ValidationTestData
{
    /// <summary>Rich text that <c>RichTextRules.HasActiveContent</c> flags.</summary>
    public const string Script = "<p>hi</p><script>alert(1)</script>";

    /// <summary>A string of exactly <paramref name="length"/> characters.</summary>
    public static string S(int length) => new('a', length);

    public static IReadOnlyList<Guid> Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    public static List<string> Strings(int count, int length = 5) =>
        Enumerable.Range(0, count).Select(i => (i.ToString() + new string('a', length)).Substring(0, length)).ToList();

    public static Stream EmptyStream() => new MemoryStream();
}
