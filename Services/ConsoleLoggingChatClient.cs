using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ProcessDataAI.Services;

/// <summary>
/// Logs AI request/response metadata without exposing prompts, model output, or binary content.
/// </summary>
public sealed class ConsoleLoggingChatClient(IChatClient innerClient, ILogger<ConsoleLoggingChatClient> logger)
    : DelegatingChatClient(innerClient)
{
    /// <summary>
    /// Logs request metadata without logging document text or model output.
    /// </summary>
    /// <param name="messages">The messages sent to the chat model.</param>
    /// <param name="options">Optional chat request options.</param>
    /// <param name="cancellationToken">A token used to cancel the request.</param>
    /// <returns>The chat model response.</returns>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatMessage[] requestMessages = messages.ToArray();
        logger.LogInformation("AI request contains {MessageCount} message(s)", requestMessages.Length);

        foreach (ChatMessage message in requestMessages)
        {
            logger.LogInformation(
                "AI request role: {Role}; content items: {ContentCount}; text characters: {TextCharacterCount}",
                message.Role,
                message.Contents.Count,
                message.Text.Length);
            foreach (AIContent content in message.Contents)
            {
                if (content is DataContent dataContent)
                {
                    logger.LogInformation(
                        "AI request binary content: {MediaType}; {ByteCount} bytes; image: {IsImage}",
                        dataContent.MediaType,
                        dataContent.Data.Length,
                        dataContent.HasTopLevelMediaType("image"));
                }
                else if (content is not TextContent)
                {
                    logger.LogInformation("AI request content type: {ContentType}", content.GetType().Name);
                }
            }
        }

        ChatResponse response = await base.GetResponseAsync(requestMessages, options, cancellationToken);
        logger.LogInformation(
            "AI response from {ModelId}; text characters: {TextCharacterCount}",
            response.ModelId ?? "unknown",
            response.Text.Length);
        return response;
    }
}
