using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using ProcessDataAI.Configuration;
using System.ClientModel;

namespace ProcessDataAI.Services;

public sealed class ChatClientFactory(
    IOptions<AiOptions> options,
    ILogger<ChatClientFactory> logger)
{
    public IChatClient Create()
    {
        AiOptions settings = options.Value;
        if (settings.Provider.Equals(AiOptions.AzureProvider, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Using Azure OpenAI chat deployment {Model}", settings.Azure.ChatModel);
            var azureClient = new AzureOpenAIClient(
                new Uri(settings.Azure.Endpoint),
                new AzureKeyCredential(settings.Azure.ApiKey));
            return azureClient.GetChatClient(settings.Azure.ChatModel).AsIChatClient();
        }

        logger.LogInformation("Using Ollama chat model {Model}", settings.Ollama.ChatModel);
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(settings.Ollama.Endpoint) };
        var ollamaClient = new OpenAIClient(new ApiKeyCredential("not-required"), clientOptions);
        return ollamaClient.GetChatClient(settings.Ollama.ChatModel).AsIChatClient();
    }
}
