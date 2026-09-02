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
/// Creates chat clients for the configured AI provider.
/// </summary>
public sealed class ChatClientFactory(
    IOptions<AiOptions> options,
    ILogger<ChatClientFactory> logger)
{
    /// <summary>
    /// Creates a chat client configured for Azure OpenAI, an OpenAI-compatible API, or Ollama.
    /// </summary>
    /// <returns>The configured chat client.</returns>
    public IChatClient Create()
    {
        AiOptions settings = options.Value;
        if (settings.Provider.Equals(AiOptions.AzureProvider, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Using Azure OpenAI chat deployment {Model}", settings.Azure.ChatModel);
            var azureClient = new AzureOpenAIClient(
                new Uri(settings.Azure.ChatEndpoint),
                new AzureKeyCredential(settings.Azure.ApiKey));
            return azureClient.GetChatClient(settings.Azure.ChatModel).AsIChatClient();
        }

        if (settings.Provider.Equals(AiOptions.OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Using OpenAI-compatible chat model {Model}", settings.OpenAI.ChatModel);
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(settings.OpenAI.ChatEndpoint) };
            var client = new OpenAIClient(
                new ApiKeyCredential(GetApiKey(settings.OpenAI.ApiKey)),
                clientOptions);
            return client.GetChatClient(settings.OpenAI.ChatModel).AsIChatClient();
        }

        if (settings.Provider.Equals(AiOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Using Ollama chat model {Model}", settings.Ollama.ChatModel);
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = OpenAiCompatibleEndpoint.EnsureV1Path(settings.Ollama.ChatEndpoint)
            };
            var ollamaClient = new OpenAIClient(new ApiKeyCredential("not-required"), clientOptions);
            return ollamaClient.GetChatClient(settings.Ollama.ChatModel).AsIChatClient();
        }

        throw new InvalidOperationException($"Unsupported AI provider '{settings.Provider}'.");
    }

    private static string GetApiKey(string apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? "not-required" : apiKey;
}
