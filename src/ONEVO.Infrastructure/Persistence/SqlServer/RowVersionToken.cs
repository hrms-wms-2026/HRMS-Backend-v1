using System.Globalization;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>
/// Client-facing form of a rowversion column. A method call on purpose: inside a final EF
/// projection it runs in the app, whereas ulong.ToString() would translate to a SQL cast of the
/// binary value and return garbage.
/// </summary>
public static class RowVersionToken
{
    public static string ToToken(ulong version) => version.ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string? token, out ulong version)
        => ulong.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out version);
}
