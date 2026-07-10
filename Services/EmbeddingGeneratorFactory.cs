using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using ProcessDataAI.Configuration;
using System.ClientModel;

namespace ProcessDataAI.Services;

public sealed class EmbeddingGeneratorFactory(
    IOptions<AiOptions> options,
    ILogger<EmbeddingGeneratorFactory> logger)
{
    public IEmbeddingGenerator<string, Embedding<float>> Create()
    {
        AiOptions settings = options.Value;
        logger.LogInformation("Loaded AI provider: {Provider}", settings.Provider);

        if (settings.Provider.Equals(AiOptions.AzureProvider, StringComparison.OrdinalIgnoreCase))
        {
            AzureOpenAiOptions azure = settings.Azure;
            var client = new AzureOpenAIClient(
                new Uri(azure.Endpoint),
                new AzureKeyCredential(azure.ApiKey));
            return client.GetEmbeddingClient(azure.EmbeddingModel).AsIEmbeddingGenerator();
        }

        OllamaOptions ollama = settings.Ollama;
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(ollama.Endpoint) };
        var ollamaClient = new OpenAIClient(new ApiKeyCredential("not-required"), clientOptions);
        return ollamaClient.GetEmbeddingClient(ollama.EmbeddingModel).AsIEmbeddingGenerator();
    }
}
