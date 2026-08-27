using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ProcessDataAI.Testing;

/// <summary>
/// Provides end-to-end validation of the local HTTPS MCP server.
/// </summary>
internal static class McpSmokeTest
{
    /// <summary>
    /// Verifies that the server is reachable through its HTTPS health endpoint.
    /// </summary>
    /// <param name="serverBaseAddress">The HTTPS base address of the test server.</param>
    /// <param name="cancellationToken">A token used to cancel the check.</param>
    /// <returns>A task that completes when the health endpoint is ready.</returns>
    public static async Task AssertHttpsAsync(
        Uri serverBaseAddress,
        CancellationToken cancellationToken)
    {
        using HttpClient httpClient = CreateLoopbackClient();
        string health = await httpClient.GetStringAsync(
            new Uri(serverBaseAddress, "health"),
            cancellationToken);
        if (!health.Contains("ready", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The HTTPS health endpoint was not ready.");
        }
    }

    /// <summary>
    /// Verifies the Streamable HTTP MCP tools, their response schema, document access, and disabled legacy SSE route.
    /// </summary>
    /// <param name="serverBaseAddress">The HTTPS base address of the test server.</param>
    /// <param name="cancellationToken">A token used to cancel the smoke test.</param>
    /// <returns>A task that completes when all assertions pass.</returns>
    public static async Task RunAsync(Uri serverBaseAddress, CancellationToken cancellationToken)
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

        string id = results.Current.GetProperty("id").GetString() ??
            throw new InvalidOperationException("The MCP search result contained no document ID.");
        if (string.IsNullOrWhiteSpace(results.Current.GetProperty("title").GetString()) ||
            string.IsNullOrWhiteSpace(results.Current.GetProperty("url").GetString()))
        {
            throw new InvalidOperationException("The MCP search result did not match the search compatibility schema.");
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
            string.IsNullOrWhiteSpace(fetched.GetProperty("text").GetString()) ||
            fetched.GetProperty("metadata").ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The fetch tool did not return the requested full document.");
        }

        string documentUrl = fetched.GetProperty("url").GetString() ??
            throw new InvalidOperationException("The fetch tool returned no citation URL.");
        string fetchedOverHttps = await httpClient.GetStringAsync(documentUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(fetchedOverHttps))
        {
            throw new InvalidOperationException("The HTTPS document URL returned no content.");
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
            $"searchResults>=1, fetchedId={id}, httpsDocument=ok, legacySse=disabled");
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
}
