namespace ProcessDataAI.Configuration;

/// <summary>
/// Loads simple <c>KEY=VALUE</c> configuration files.
/// </summary>
public static class EnvFile
{
    /// <summary>
    /// Reads configuration entries from an environment file.
    /// </summary>
    /// <param name="path">The path to the environment file.</param>
    /// <returns>The parsed settings, keyed without regard to case.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="path"/> does not exist.</exception>
    /// <exception cref="FormatException">Thrown when an entry is not in <c>KEY=VALUE</c> form.</exception>
    public static IReadOnlyDictionary<string, string?> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Configuration file '{path}' was not found. Copy .env.example to .env and configure an AI provider.",
                path);
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new FormatException($"Invalid .env entry: '{rawLine}'. Expected KEY=VALUE.");
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }
}
