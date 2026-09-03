using System.Text;
using Microsoft.Extensions.DataIngestion;

namespace ProcessDataAI.Ingestion;

/// <summary>
/// Reads UTF-8 Markdown and plain-text files into an ingestion document.
/// </summary>
public sealed class TextDocumentReader : IngestionDocumentReader
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

        using var reader = new StreamReader(
            source,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        string text = (await reader.ReadToEndAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(
                $"Text file '{Path.GetFileName(identifier)}' contains no content.");
        }

        var section = new IngestionDocumentSection();
        section.Elements.Add(new IngestionDocumentParagraph(text));

        var document = new IngestionDocument(identifier);
        document.Sections.Add(section);
        return document;
    }
}
