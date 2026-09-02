using Microsoft.Extensions.Options;

namespace ProcessDataAI.Configuration;

/// <summary>
/// Validates that the selected AI provider has the endpoint, credentials, and models it requires.
/// </summary>
public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    /// <summary>
    /// Validates the configured AI provider settings.
    /// </summary>
    /// <param name="name">The options instance name.</param>
    /// <param name="options">The configured AI options.</param>
    /// <returns>A successful result or a description of the invalid configuration.</returns>
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        if (options.Provider.Equals(AiOptions.AzureProvider, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRequired(
                ("AZURE_OPENAI_EMBEDDING_ENDPOINT", options.Azure.EmbeddingEndpoint),
                ("AZURE_OPENAI_CHAT_ENDPOINT", options.Azure.ChatEndpoint),
                ("AZURE_OPENAI_API_KEY", options.Azure.ApiKey),
                ("AZURE_OPENAI_EMBEDDING_MODEL", options.Azure.EmbeddingModel),
                ("AZURE_OPENAI_CHAT_MODEL", options.Azure.ChatModel));
        }

        if (options.Provider.Equals(AiOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRequired(
                ("OLLAMA_EMBEDDING_ENDPOINT", options.Ollama.EmbeddingEndpoint),
                ("OLLAMA_CHAT_ENDPOINT", options.Ollama.ChatEndpoint),
                ("OLLAMA_EMBEDDING_MODEL", options.Ollama.EmbeddingModel),
                ("OLLAMA_CHAT_MODEL", options.Ollama.ChatModel));
        }

        if (options.Provider.Equals(AiOptions.OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRequired(
                ("OPENAI_EMBEDDING_ENDPOINT", options.OpenAI.EmbeddingEndpoint),
                ("OPENAI_CHAT_ENDPOINT", options.OpenAI.ChatEndpoint),
                ("OPENAI_EMBEDDING_MODEL", options.OpenAI.EmbeddingModel),
                ("OPENAI_CHAT_MODEL", options.OpenAI.ChatModel));
        }

        return ValidateOptionsResult.Fail(
            "AI_PROVIDER must be 'Azure', 'OpenAI', or 'Ollama' in the .env file.");
    }

    private static ValidateOptionsResult ValidateRequired(params (string Name, string Value)[] settings)
    {
        string[] missing = settings
            .Where(setting => string.IsNullOrWhiteSpace(setting.Value))
            .Select(setting => setting.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            return ValidateOptionsResult.Fail($"Missing required provider configuration: {string.Join(", ", missing)}.");
        }

        string[] invalidEndpoints = settings
            .Where(setting => setting.Name.EndsWith("ENDPOINT", StringComparison.Ordinal))
            .Where(setting => !Uri.TryCreate(setting.Value, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            .Select(setting => setting.Name)
            .ToArray();
        if (invalidEndpoints.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"The configured provider endpoints must be absolute HTTP or HTTPS URLs: {string.Join(", ", invalidEndpoints)}.");
        }

        return ValidateOptionsResult.Success;
    }
}
