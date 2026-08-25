using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ProcessDataAI.Services;

/// <summary>
/// Logs the semantic request sent to an AI model without exposing binary image data.
/// Intended for POC troubleshooting only: prompts and model output can contain document data.
/// </summary>
public sealed class ConsoleLoggingChatClient(IChatClient innerClient, ILogger<ConsoleLoggingChatClient> logger)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatMessage[] requestMessages = messages.ToArray();
        logger.LogInformation("AI request contains {MessageCount} message(s)", requestMessages.Length);

        foreach (ChatMessage message in requestMessages)
        {
            logger.LogInformation("AI request role: {Role}; text: {Text}", message.Role, message.Text);
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
        logger.LogInformation("AI response from {ModelId}: {Text}", response.ModelId ?? "unknown", response.Text);
        return response;
    }
}
