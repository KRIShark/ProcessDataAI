using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ProcessDataAI.Configuration;
using ProcessDataAI.Services;

namespace ProcessDataAI.Mcp;

[McpServerToolType]
/// <summary>
/// Exposes semantic document search and full-document retrieval through MCP.
/// </summary>
public sealed class RagMcpTools(
    DocumentSearchService searchService,
    DocumentCatalog catalog,
    IOptions<RagMcpOptions> options)
{
    [McpServerTool(
        Name = "search",
        Title = "Search documents",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Find documents relevant to a text query. Returns a collection containing each resource ID and title, with an optional URL, text preview, and metadata. Use fetch with an ID to retrieve the complete resource.")]
    /// <summary>
    /// Finds indexed documents relevant to a text query.
    /// </summary>
    /// <param name="query">The natural-language query to search for.</param>
    /// <param name="cancellationToken">A token used to cancel the search.</param>
    /// <returns>Matching resources with IDs, titles, and optional URLs, previews, and metadata.</returns>
    public async Task<SearchToolOutput> SearchAsync(
        [Description("Natural-language query used to find relevant documents.")] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchToolOutput([]);
        }

        if (query.Length > 4_000)
        {
            throw new McpException("The search query must not exceed 4000 characters.");
        }

        IReadOnlyList<DocumentSearchMatch> matches = await searchService.SearchDocumentsAsync(
            query,
            maxResults: 5,
            cancellationToken);
        SearchResultItem[] results = matches
            .Select(match => new SearchResultItem(
                match.Id,
                match.Title,
                CreateDocumentUrl(match.Id),
                match.Text,
                CreateMetadata(match.Title, match.MediaType, match.SizeBytes)))
            .ToArray();
        return new SearchToolOutput(results);
    }

    [McpServerTool(
        Name = "fetch",
        Title = "Fetch document",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieve the full textual content of a specific resource using the stable ID returned by search. Returns the required ID, title, and text, with an optional URL and metadata.")]
    /// <summary>
    /// Retrieves the complete extracted content for a specific indexed resource.
    /// </summary>
    /// <param name="id">The stable document ID returned by <see cref="SearchAsync"/>.</param>
    /// <returns>The complete document content and citation metadata.</returns>
    public FetchToolOutput Fetch(
        [Description("Stable document ID returned by the search tool.")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new McpException("A document ID is required.");
        }

        if (!catalog.TryGetById(id, out CatalogDocument document))
        {
            throw new McpException($"Document '{id}' was not found.");
        }

        return new FetchToolOutput(
            document.Id,
            document.Title,
            document.Text,
            CreateDocumentUrl(document.Id),
            CreateMetadata(document.Title, document.MediaType, document.SizeBytes));
    }

    private string CreateDocumentUrl(string id)
    {
        var baseUri = new Uri(options.Value.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, $"documents/{Uri.EscapeDataString(id)}").AbsoluteUri;
    }

    private static IReadOnlyDictionary<string, object?> CreateMetadata(
        string fileName,
        string mediaType,
        long sizeBytes) =>
        new Dictionary<string, object?>
        {
            ["file_name"] = fileName,
            ["media_type"] = mediaType,
            ["size_bytes"] = sizeBytes,
        };
}

/// <summary>
/// Represents the OpenAI-compatible response returned by the MCP <c>search</c> tool.
/// </summary>
public sealed record SearchToolOutput(
    [property: JsonPropertyName("results")] IReadOnlyList<SearchResultItem> Results);

/// <summary>
/// Represents one resource discovered by the search tool.
/// </summary>
public sealed record SearchResultItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Url = null,
    [property: JsonPropertyName("text")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Text = null,
    [property: JsonPropertyName("metadata")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object?>? Metadata = null);

/// <summary>
/// Represents the OpenAI-compatible response returned by the MCP <c>fetch</c> tool.
/// </summary>
public sealed record FetchToolOutput(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("url")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Url = null,
    [property: JsonPropertyName("metadata")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object?>? Metadata = null);
