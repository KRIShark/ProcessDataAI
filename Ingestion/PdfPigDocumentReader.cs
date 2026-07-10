using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ProcessDataAI.Ingestion;

public sealed class PdfPigDocumentReader(ILogger<PdfPigDocumentReader> logger) : IngestionDocumentReader
{
    public override Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string? mediaType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        try
        {
            using PdfDocument pdf = PdfDocument.Open(source);
            var document = new IngestionDocument(identifier);

            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = ContentOrderTextExtractor.GetText(page).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var section = new IngestionDocumentSection();
                section.Elements.Add(new IngestionDocumentParagraph(text) { PageNumber = page.Number });
                document.Sections.Add(section);
            }

            if (document.Sections.Count == 0)
            {
                throw new InvalidDataException(
                    $"PDF '{identifier}' contains no extractable text. Scanned PDFs require OCR, which this sample intentionally does not use.");
            }

            logger.LogInformation(
                "Extracted text from {PageCount} page(s) in {DocumentName}",
                document.Sections.Count,
                identifier);
            return Task.FromResult(document);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InvalidDataException)
        {
            throw new InvalidDataException($"Could not read PDF '{identifier}': {exception.Message}", exception);
        }
    }
}
