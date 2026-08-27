namespace ProcessDataAI.Configuration;

/// <summary>
/// Configures public URLs returned by the RAG MCP tools.
/// </summary>
public sealed class RagMcpOptions
{
    /// <summary>Gets or sets the HTTPS base URL used for document citation links.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
