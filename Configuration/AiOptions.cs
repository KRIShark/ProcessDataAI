namespace ProcessDataAI.Configuration;

/// <summary>
/// Configures the AI provider and its provider-specific connection settings.
/// </summary>
public sealed class AiOptions
{
    /// <summary>Identifies Azure OpenAI as the selected provider.</summary>
    public const string AzureProvider = "Azure";
    /// <summary>Identifies an OpenAI-compatible endpoint as the selected provider.</summary>
    public const string OpenAiProvider = "OpenAI";
    /// <summary>Identifies Ollama as the selected provider.</summary>
    public const string OllamaProvider = "Ollama";

    /// <summary>Gets or sets the active AI provider name.</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Gets or sets the Azure OpenAI settings.</summary>
    public AzureOpenAiOptions Azure { get; set; } = new();
    /// <summary>Gets or sets the OpenAI-compatible endpoint settings.</summary>
    public OpenAiOptions OpenAI { get; set; } = new();
    /// <summary>Gets or sets the Ollama settings.</summary>
    public OllamaOptions Ollama { get; set; } = new();
}

/// <summary>
/// Connection and model settings for Azure OpenAI.
/// </summary>
public sealed class AzureOpenAiOptions
{
    /// <summary>Gets or sets the Azure OpenAI endpoint.</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>Gets or sets the Azure OpenAI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Gets or sets the embedding deployment name.</summary>
    public string EmbeddingModel { get; set; } = string.Empty;
    /// <summary>Gets or sets the chat deployment name.</summary>
    public string ChatModel { get; set; } = string.Empty;
}

/// <summary>
/// Connection and model settings for Ollama's OpenAI-compatible API.
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>Gets or sets the Ollama endpoint.</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>Gets or sets the embedding model name.</summary>
    public string EmbeddingModel { get; set; } = string.Empty;
    /// <summary>Gets or sets the chat model name.</summary>
    public string ChatModel { get; set; } = string.Empty;
}

/// <summary>
/// Connection and model settings for an OpenAI-compatible API.
/// </summary>
public sealed class OpenAiOptions
{
    /// <summary>Gets or sets the API endpoint.</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>Gets or sets the API key, when the endpoint requires one.</summary>
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Gets or sets the embedding model name.</summary>
    public string EmbeddingModel { get; set; } = string.Empty;
    /// <summary>Gets or sets the chat model name.</summary>
    public string ChatModel { get; set; } = string.Empty;
}
