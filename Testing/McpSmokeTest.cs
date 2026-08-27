using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ProcessDataAI.Testing;

/// <summary>
/// Provides end-to-end validation of the local HTTP or HTTPS MCP server.
/// </summary>
internal static class McpSmokeTest
{
    /// <summary>
    /// Verifies that the server is reachable through its health endpoint.
    /// </summary>
    /// <param name="serverBaseAddress">The HTTP or HTTPS base address of the test server.</param>
    /// <param name="cancellationToken">A token used to cancel the check.</param>
    /// <returns>A task that completes when the health endpoint is ready.</returns>
    public static async Task AssertServerAsync(
        Uri serverBaseAddress,
        CancellationToken cancellationToken)
    {
        using HttpClient httpClient = CreateLoopbackClient();
        string health = await httpClient.GetStringAsync(
            new Uri(serverBaseAddress, "health"),
            cancellationToken);
        if (!health.Contains("ready", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The MCP health endpoint was not ready.");
        }
    }

    /// <summary>
    /// Verifies the Streamable HTTP MCP tools, their response schema, document access, and disabled legacy SSE route.
    /// </summary>
    /// <param name="serverBaseAddress">The HTTP or HTTPS base address of the test server.</param>
    /// <param name="imageDocumentId">An optional document ID whose fetched text must contain generated image descriptions.</param>
    /// <param name="cancellationToken">A token used to cancel the smoke test.</param>
    /// <returns>A task that completes when all assertions pass.</returns>
    public static async Task RunAsync(
        Uri serverBaseAddress,
        string? imageDocumentId,
        CancellationToken cancellationToken)
    {
        using HttpClient httpClient = CreateLoopbackClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Name = "ProcessDataAI smoke test",
                Endpoint = new Uri(serverBaseAddress, "mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
            },
            httpClient);

        await using McpClient client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        string[] toolNames = tools.Select(tool => tool.Name).Order().ToArray();
        if (!toolNames.SequenceEqual(["fetch", "search"]))
        {
            throw new InvalidOperationException(
                $"Expected MCP tools 'fetch' and 'search', received: {string.Join(", ", toolNames)}.");
        }

        McpClientTool searchTool = tools.Single(tool => tool.Name == "search");
        JsonElement searchOutputSchema = searchTool.ProtocolTool.OutputSchema ??
            throw new InvalidOperationException("The search tool exposed no output schema.");
        AssertRequiredProperties(searchOutputSchema, "results");
        JsonElement searchItemSchema = searchOutputSchema
            .GetProperty("properties")
            .GetProperty("results")
            .GetProperty("items");
        AssertRequiredProperties(searchItemSchema, "id", "title");

        McpClientTool fetchTool = tools.Single(tool => tool.Name == "fetch");
        JsonElement fetchOutputSchema = fetchTool.ProtocolTool.OutputSchema ??
            throw new InvalidOperationException("The fetch tool exposed no output schema.");
        AssertRequiredProperties(fetchOutputSchema, "id", "text", "title");

        CallToolResult searchResult = await client.CallToolAsync(
            "search",
            new Dictionary<string, object?> { ["query"] = "employee vacation days" },
            cancellationToken: cancellationToken);
        JsonElement searchContent = searchResult.StructuredContent ??
            throw new InvalidOperationException("The search tool returned no structured content.");
        JsonElement.ArrayEnumerator results = searchContent.GetProperty("results").EnumerateArray();
        if (!results.MoveNext())
        {
            throw new InvalidOperationException("The MCP search tool returned no documents.");
        }

        JsonElement firstSearchResult = results.Current;
        string id = firstSearchResult.GetProperty("id").GetString() ??
            throw new InvalidOperationException("The MCP search result contained no document ID.");
        if (string.IsNullOrWhiteSpace(firstSearchResult.GetProperty("title").GetString()))
        {
            throw new InvalidOperationException("The MCP search result contained no title.");
        }
        if (!firstSearchResult.TryGetProperty("text", out JsonElement searchText) ||
            string.IsNullOrWhiteSpace(searchText.GetString()))
        {
            throw new InvalidOperationException("The MCP search result contained no text preview.");
        }
        if (firstSearchResult.TryGetProperty("url", out JsonElement searchUrl) &&
            string.IsNullOrWhiteSpace(searchUrl.GetString()))
        {
            throw new InvalidOperationException("The MCP search result contained an empty optional URL.");
        }
        if (firstSearchResult.TryGetProperty("metadata", out JsonElement searchMetadata) &&
            searchMetadata.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The MCP search result metadata was not an object.");
        }
        if (searchResult.Content.OfType<TextContentBlock>().All(block => string.IsNullOrWhiteSpace(block.Text)))
        {
            throw new InvalidOperationException("The search tool returned no JSON compatibility text.");
        }

        CallToolResult fetchResult = await client.CallToolAsync(
            "fetch",
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken: cancellationToken);
        JsonElement fetched = fetchResult.StructuredContent ??
            throw new InvalidOperationException("The fetch tool returned no structured content.");
        if (fetched.GetProperty("id").GetString() != id ||
            string.IsNullOrWhiteSpace(fetched.GetProperty("title").GetString()) ||
            string.IsNullOrWhiteSpace(fetched.GetProperty("text").GetString()))
        {
            throw new InvalidOperationException("The fetch tool did not return the requested full document.");
        }

        if (fetched.TryGetProperty("metadata", out JsonElement fetchMetadata) &&
            fetchMetadata.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The fetch metadata was not an object.");
        }

        if (fetched.TryGetProperty("url", out JsonElement fetchedUrl))
        {
            string documentUrl = fetchedUrl.GetString() ??
                throw new InvalidOperationException("The fetch tool returned an empty optional URL.");
            string fetchedDocument = await httpClient.GetStringAsync(documentUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(fetchedDocument))
            {
                throw new InvalidOperationException("The document URL returned no content.");
            }
        }

        if (imageDocumentId is not null)
        {
            CallToolResult imageFetchResult = await client.CallToolAsync(
                "fetch",
                new Dictionary<string, object?> { ["id"] = imageDocumentId },
                cancellationToken: cancellationToken);
            string imageDocumentText = imageFetchResult.StructuredContent?
                .GetProperty("text")
                .GetString() ?? string.Empty;
            if (!imageDocumentText.Contains("Image on page", StringComparison.Ordinal) ||
                imageDocumentText.Contains("(embedded-image)", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The fetch tool did not return the generated alternative text for embedded images.");
            }
        }

        using HttpResponseMessage legacySse = await httpClient.GetAsync(
            new Uri(serverBaseAddress, "mcp/sse"),
            cancellationToken);
        if (legacySse.StatusCode != HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"The legacy SSE endpoint should be disabled but returned {(int)legacySse.StatusCode}.");
        }

        Console.WriteLine(
            $"MCP SMOKE TEST PASSED: tools=[{string.Join(",", toolNames)}], " +
            $"searchResults>=1, fetchedId={id}, transport={serverBaseAddress.Scheme}, " +
            "searchPreview=ok, optionalFields=valid, fullFetch=ok, imageAlternativeText=ok, " +
            "legacySse=disabled");
    }

    private static HttpClient CreateLoopbackClient()
    {
        var handler = new HttpClientHandler
        {
            // The smoke test creates an in-memory certificate that is never trusted
            // or persisted. This bypass applies only to this loopback test client.
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler);
    }

    private static void AssertRequiredProperties(JsonElement schema, params string[] expectedProperties)
    {
        string[] actualProperties = schema.TryGetProperty("required", out JsonElement required)
            ? required.EnumerateArray()
                .Select(property => property.GetString() ?? string.Empty)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        string[] expected = expectedProperties.Order(StringComparer.Ordinal).ToArray();
        if (!actualProperties.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Expected required output properties [{string.Join(",", expected)}], " +
                $"received [{string.Join(",", actualProperties)}].");
        }
    }
}
