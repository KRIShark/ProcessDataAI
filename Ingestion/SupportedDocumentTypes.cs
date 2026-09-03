namespace ProcessDataAI.Ingestion;

/// <summary>
/// Defines the source file extensions and media types supported by the ingestion pipeline.
/// </summary>
public static class SupportedDocumentTypes
{
    private static readonly IReadOnlyDictionary<string, string> MediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".md"] = "text/markdown",
            [".markdown"] = "text/markdown",
            [".txt"] = "text/plain",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
        };

    /// <summary>
    /// Gets the supported extensions in display order.
    /// </summary>
    public static IReadOnlyList<string> Extensions { get; } = MediaTypes.Keys.ToArray();

    /// <summary>
    /// Determines whether a file extension is supported.
    /// </summary>
    public static bool IsSupported(string extension) => MediaTypes.ContainsKey(extension);

    /// <summary>
    /// Gets the media type for a supported file extension.
    /// </summary>
    public static bool TryGetMediaType(string extension, out string mediaType) =>
        MediaTypes.TryGetValue(extension, out mediaType!);
}
