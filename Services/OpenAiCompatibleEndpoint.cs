namespace ProcessDataAI.Services;

internal static class OpenAiCompatibleEndpoint
{
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
