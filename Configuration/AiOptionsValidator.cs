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
                ("AZURE_OPENAI_ENDPOINT", options.Azure.Endpoint),
                ("AZURE_OPENAI_API_KEY", options.Azure.ApiKey),
                ("AZURE_OPENAI_EMBEDDING_MODEL", options.Azure.EmbeddingModel),
                ("AZURE_OPENAI_CHAT_MODEL", options.Azure.ChatModel));
        }

        if (options.Provider.Equals(AiOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRequired(
                ("OLLAMA_ENDPOINT", options.Ollama.Endpoint),
                ("OLLAMA_EMBEDDING_MODEL", options.Ollama.EmbeddingModel),
                ("OLLAMA_CHAT_MODEL", options.Ollama.ChatModel));
        }

        if (options.Provider.Equals(AiOptions.OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateRequired(
                ("OPENAI_ENDPOINT", options.OpenAI.Endpoint),
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

        string? endpoint = settings.FirstOrDefault(setting => setting.Name.EndsWith("ENDPOINT", StringComparison.Ordinal)).Value;
        if (endpoint is not null && (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            return ValidateOptionsResult.Fail("The configured provider endpoint must be an absolute HTTP or HTTPS URL.");
        }

        return ValidateOptionsResult.Success;
    }
}
