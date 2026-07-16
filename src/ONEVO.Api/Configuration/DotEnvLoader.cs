namespace ONEVO.Api.Configuration;

/// <summary>
/// Minimal `.env` loader. Reads KEY=VALUE pairs from the first `.env` found in
/// the current directory or any parent (up to a hard limit) and copies them
/// into the process environment so ASP.NET Core's default configuration
/// pipeline can consume them. Existing process env variables WIN — i.e.
/// shell / docker-compose / launchSettings overrides are never clobbered.
///
/// Format:
///   - one KEY=VALUE per line
///   - blank lines and lines starting with '#' are ignored
///   - surrounding single or double quotes around the value are stripped
///   - we deliberately do not support multi-line, escapes, or interpolation;
///     keep your secret values single-line
/// </summary>
public static class DotEnvLoader
{
    public static void LoadIfPresent(string fileName = ".env", int maxParentDepth = 4)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; depth <= maxParentDepth && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (!File.Exists(candidate)) continue;

            ApplyFile(candidate);
            return;
        }
    }

    private static void ApplyFile(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            // Process env wins; do not overwrite an explicit shell/docker value.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                continue;

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
