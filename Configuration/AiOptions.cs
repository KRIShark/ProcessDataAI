namespace ProcessDataAI.Configuration;

public sealed class AiOptions
{
    public const string AzureProvider = "Azure";
    public const string OllamaProvider = "Ollama";

    public string Provider { get; set; } = string.Empty;
    public AzureOpenAiOptions Azure { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
}

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
}

public sealed class OllamaOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
}
