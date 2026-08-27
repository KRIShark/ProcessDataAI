namespace ProcessDataAI.Services;

/// <summary>
/// Normalizes OpenAI-compatible endpoints to the API's <c>/v1</c> base path.
/// </summary>
internal static class OpenAiCompatibleEndpoint
{
    /// <summary>
    /// Ensures that an endpoint URI ends with <c>/v1</c>.
    /// </summary>
    /// <param name="endpoint">The configured endpoint URI.</param>
    /// <returns>An endpoint URI with the <c>/v1</c> path.</returns>
    public static Uri EnsureV1Path(string endpoint)
    {
        var uri = new Uri(endpoint, UriKind.Absolute);
        string path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Path = $"{path}/v1"
        };
        return builder.Uri;
    }
}
