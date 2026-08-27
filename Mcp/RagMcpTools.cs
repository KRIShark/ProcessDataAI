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
    [Description("Search the indexed PDF documents using semantic similarity. Returns stable document IDs, filenames, and citation URLs. Use fetch with an ID to retrieve the complete document text.")]
    /// <summary>
    /// Searches indexed documents and returns stable IDs with citation metadata.
    /// </summary>
    /// <param name="query">The natural-language query to search for.</param>
    /// <param name="cancellationToken">A token used to cancel the search.</param>
    /// <returns>The matching document IDs, titles, and HTTPS URLs.</returns>
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
                CreateDocumentUrl(match.Id)))
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
    [Description("Fetch the complete extracted text and metadata for one indexed PDF using the stable document ID returned by search.")]
    /// <summary>
    /// Retrieves the complete extracted content for an indexed document.
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
            new Dictionary<string, object?>
            {
                ["file_name"] = document.Title,
                ["media_type"] = document.MediaType,
                ["size_bytes"] = document.SizeBytes,
            });
    }

    private string CreateDocumentUrl(string id)
    {
        var baseUri = new Uri(options.Value.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, $"documents/{Uri.EscapeDataString(id)}").AbsoluteUri;
    }
}

/// <summary>
/// Represents the OpenAI-compatible response returned by the MCP <c>search</c> tool.
/// </summary>
public sealed record SearchToolOutput(
    [property: JsonPropertyName("results")] IReadOnlyList<SearchResultItem> Results);

/// <summary>
/// Represents one searchable document and its HTTPS citation link.
/// </summary>
public sealed record SearchResultItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url);

/// <summary>
/// Represents the OpenAI-compatible response returned by the MCP <c>fetch</c> tool.
/// </summary>
public sealed record FetchToolOutput(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, object?> Metadata);
