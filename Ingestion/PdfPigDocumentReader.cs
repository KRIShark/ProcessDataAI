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
                var section = new IngestionDocumentSection();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    section.Elements.Add(new IngestionDocumentParagraph(text) { PageNumber = page.Number });
                }

                int imageNumber = 0;
                foreach (var pdfImage in page.GetImages())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!pdfImage.TryGetPng(out byte[]? pngContent))
                    {
                        logger.LogWarning(
                            "Skipped an unsupported image on page {PageNumber} of {DocumentName}",
                            page.Number,
                            identifier);
                        continue;
                    }

                    imageNumber++;
                    section.Elements.Add(new IngestionDocumentImage(
                        $"![Image {imageNumber} on page {page.Number}](embedded-image)")
                    {
                        Content = pngContent,
                        MediaType = "image/png",
                        PageNumber = page.Number
                    });
                }

                if (section.Elements.Count > 0)
                {
                    document.Sections.Add(section);
                }
            }

            if (document.Sections.Count == 0)
            {
                throw new InvalidDataException(
                    $"PDF '{identifier}' contains no extractable text or supported images. Scanned text requires OCR, which this sample intentionally does not use.");
            }

            logger.LogInformation(
                "Extracted content from {PageCount} page(s) in {DocumentName}",
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
