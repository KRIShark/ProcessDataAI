using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using ProcessDataAI.Configuration;
using System.ClientModel;

namespace ProcessDataAI.Services;

/// <summary>
/// Creates embedding generators for the configured AI provider.
/// </summary>
public sealed class EmbeddingGeneratorFactory(
    IOptions<AiOptions> options,
    ILogger<EmbeddingGeneratorFactory> logger)
{
    /// <summary>
    /// Creates an embedding generator configured for Azure OpenAI, an OpenAI-compatible API, or Ollama.
    /// </summary>
    /// <returns>The configured embedding generator.</returns>
    public IEmbeddingGenerator<string, Embedding<float>> Create()
    {
        AiOptions settings = options.Value;
        logger.LogInformation("Loaded AI provider: {Provider}", settings.Provider);

        if (settings.Provider.Equals(AiOptions.AzureProvider, StringComparison.OrdinalIgnoreCase))
        {
            AzureOpenAiOptions azure = settings.Azure;
            var client = new AzureOpenAIClient(
                new Uri(azure.EmbeddingEndpoint),
                new AzureKeyCredential(azure.ApiKey));
            return client.GetEmbeddingClient(azure.EmbeddingModel).AsIEmbeddingGenerator();
        }

        if (settings.Provider.Equals(AiOptions.OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            OpenAiOptions openAI = settings.OpenAI;
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(openAI.EmbeddingEndpoint) };
            var client = new OpenAIClient(
                new ApiKeyCredential(GetApiKey(openAI.ApiKey)),
                clientOptions);
            return client.GetEmbeddingClient(openAI.EmbeddingModel).AsIEmbeddingGenerator();
        }

        if (settings.Provider.Equals(AiOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase))
        {
            OllamaOptions ollama = settings.Ollama;
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = OpenAiCompatibleEndpoint.EnsureV1Path(ollama.EmbeddingEndpoint)
            };
            var ollamaClient = new OpenAIClient(new ApiKeyCredential("not-required"), clientOptions);
            return ollamaClient.GetEmbeddingClient(ollama.EmbeddingModel).AsIEmbeddingGenerator();
        }

        throw new InvalidOperationException($"Unsupported AI provider '{settings.Provider}'.");
    }

    private static string GetApiKey(string apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? "not-required" : apiKey;
}
