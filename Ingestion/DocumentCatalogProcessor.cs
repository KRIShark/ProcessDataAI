using Microsoft.Extensions.DataIngestion;
using ProcessDataAI.Services;

namespace ProcessDataAI.Ingestion;

/// <summary>
/// Captures fully processed documents for retrieval by stable document ID.
/// </summary>
public sealed class DocumentCatalogProcessor(DocumentCatalog catalog) : IngestionDocumentProcessor
{
    /// <summary>
    /// Stores the processed document in the catalog and returns it unchanged.
    /// </summary>
    /// <param name="document">The processed document to capture.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The original <paramref name="document"/>.</returns>
    public override Task<IngestionDocument> ProcessAsync(
        IngestionDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        catalog.Capture(document);
        return Task.FromResult(document);
    }
}
