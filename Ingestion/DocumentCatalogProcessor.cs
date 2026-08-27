using Microsoft.Extensions.DataIngestion;
using ProcessDataAI.Services;

namespace ProcessDataAI.Ingestion;

public sealed class DocumentCatalogProcessor(DocumentCatalog catalog) : IngestionDocumentProcessor
{
    public override Task<IngestionDocument> ProcessAsync(
        IngestionDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        catalog.Capture(document);
        return Task.FromResult(document);
    }
}
