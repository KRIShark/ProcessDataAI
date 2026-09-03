using Microsoft.Extensions.DataIngestion;

namespace ProcessDataAI.Ingestion;

/// <summary>
/// Reads a supported standalone image into an ingestion document.
/// </summary>
public sealed class ImageDocumentReader : IngestionDocumentReader
{
    /// <inheritdoc />
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string? mediaType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (string.IsNullOrWhiteSpace(mediaType) &&
            !SupportedDocumentTypes.TryGetMediaType(Path.GetExtension(identifier), out mediaType!))
        {
            throw new InvalidDataException(
                $"Could not determine the image type for '{Path.GetFileName(identifier)}'.");
        }

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        byte[] content = buffer.ToArray();
        if (content.Length == 0)
        {
            throw new InvalidDataException(
                $"Image file '{Path.GetFileName(identifier)}' is empty.");
        }

        string fileName = Path.GetFileName(identifier);
        var image = new IngestionDocumentImage($"![{fileName}](embedded-image)")
        {
            Content = content,
            MediaType = mediaType,
        };
        var section = new IngestionDocumentSection();
        section.Elements.Add(image);

        var document = new IngestionDocument(identifier);
        document.Sections.Add(section);
        return document;
    }
}
