using Microsoft.Extensions.DataIngestion;

namespace ProcessDataAI.Ingestion;

/// <summary>
/// Routes each supported source file to its format-specific reader.
/// </summary>
public sealed class MultiFormatDocumentReader(
    PdfPigDocumentReader pdfReader,
    TextDocumentReader textReader,
    ImageDocumentReader imageReader) : IngestionDocumentReader
{
    /// <inheritdoc />
    public override Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string? mediaType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        string extension = Path.GetExtension(identifier);
        if (!SupportedDocumentTypes.TryGetMediaType(extension, out string detectedMediaType))
        {
            throw new NotSupportedException(
                $"File type '{extension}' is not supported for '{Path.GetFileName(identifier)}'.");
        }

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => pdfReader.ReadAsync(source, identifier, detectedMediaType, cancellationToken),
            ".md" or ".markdown" or ".txt" =>
                textReader.ReadAsync(source, identifier, detectedMediaType, cancellationToken),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" =>
                imageReader.ReadAsync(source, identifier, detectedMediaType, cancellationToken),
            _ => throw new NotSupportedException(
                $"File type '{extension}' is not supported for '{Path.GetFileName(identifier)}'."),
        };
    }
}
