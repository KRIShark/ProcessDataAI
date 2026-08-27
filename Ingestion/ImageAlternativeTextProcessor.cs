using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace ProcessDataAI.Ingestion;

/// <summary>
/// Generates alternative text one image at a time without requiring the model
/// to support structured output. This works with Azure OpenAI and with smaller
/// OpenAI-compatible Ollama vision models that do not reliably return JSON.
/// </summary>
public sealed class ImageAlternativeTextProcessor(
    IChatClient chatClient,
    ILogger<ImageAlternativeTextProcessor> logger) : IngestionDocumentProcessor
{
    /// <summary>
    /// Generates best-effort alternative text for each supported image in a document.
    /// </summary>
    /// <param name="document">The document whose images are enriched.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The enriched document.</returns>
    public override async Task<IngestionDocument> ProcessAsync(
        IngestionDocument document,
        CancellationToken cancellationToken = default)
    {
        foreach (IngestionDocumentSection section in document.Sections)
        {
            foreach (IngestionDocumentImage image in section.Elements.OfType<IngestionDocumentImage>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (image.Content is not ReadOnlyMemory<byte> imageContent ||
                    imageContent.IsEmpty ||
                    string.IsNullOrWhiteSpace(image.MediaType))
                {
                    logger.LogWarning(
                        "Skipped an image without binary content or a media type in {DocumentId}",
                        document.Identifier);
                    continue;
                }

                try
                {
                    var messages = new[]
                    {
                        new ChatMessage(
                            ChatRole.System,
                            "Write detailed alternative text for the attached image in fewer than 50 words. " +
                            "Return only the alternative text, without JSON, Markdown, or commentary."),
                        new ChatMessage(
                            ChatRole.User,
                            [
                                new TextContent("Describe this image."),
                                new DataContent(imageContent, image.MediaType),
                            ]),
                    };

                    ChatResponse response = await chatClient.GetResponseAsync(
                        messages,
                        cancellationToken: cancellationToken);
                    string alternativeText = response.Text.Trim();
                    if (string.IsNullOrWhiteSpace(alternativeText))
                    {
                        logger.LogWarning(
                            "The image model returned no alternative text for an image in {DocumentId}",
                            document.Identifier);
                        continue;
                    }

                    image.AlternativeText = alternativeText;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Image enrichment is best-effort and should not prevent the
                    // remaining document text from being ingested.
                    logger.LogWarning(
                        exception,
                        "Could not generate alternative text for an image in {DocumentId}",
                        document.Identifier);
                }
            }
        }

        return document;
    }
}
