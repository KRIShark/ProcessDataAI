using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
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
            int extractedImageCount = 0;
            int unsupportedImageCount = 0;

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
                    if (!TryGetImageContent(pdfImage, out byte[] imageContent, out string imageMediaType))
                    {
                        unsupportedImageCount++;
                        logger.LogWarning(
                            "Skipped an unsupported image on page {PageNumber} of {DocumentName}",
                            page.Number,
                            identifier);
                        continue;
                    }

                    imageNumber++;
                    extractedImageCount++;
                    section.Elements.Add(new IngestionDocumentImage(
                        $"![Image {imageNumber} on page {page.Number}](embedded-image)")
                    {
                        Content = imageContent,
                        MediaType = imageMediaType,
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
                "Extracted content from {PageCount} page(s) and {ImageCount} image(s) in {DocumentName}",
                document.Sections.Count,
                extractedImageCount,
                identifier);
            if (unsupportedImageCount > 0)
            {
                logger.LogWarning(
                    "Skipped {ImageCount} unsupported image(s) in {DocumentName}",
                    unsupportedImageCount,
                    identifier);
            }

            return Task.FromResult(document);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InvalidDataException)
        {
            throw new InvalidDataException($"Could not read PDF '{identifier}': {exception.Message}", exception);
        }
    }

    private static bool TryGetImageContent(
        IPdfImage image,
        out byte[] content,
        out string mediaType)
    {
        if (image.TryGetPng(out byte[]? pngContent) && pngContent is not null)
        {
            content = pngContent;
            mediaType = "image/png";
            return true;
        }

        // PdfPig intentionally does not convert JPEG streams in TryGetPng. DCT-encoded
        // JPEG bytes are already suitable for Azure OpenAI and Ollama image inputs.
        ReadOnlySpan<byte> rawBytes = image.RawBytes;
        if (rawBytes.Length >= 3 &&
            rawBytes[0] == 0xFF &&
            rawBytes[1] == 0xD8 &&
            rawBytes[2] == 0xFF)
        {
            content = rawBytes.ToArray();
            mediaType = "image/jpeg";
            return true;
        }

        content = [];
        mediaType = string.Empty;
        return false;
    }
}
