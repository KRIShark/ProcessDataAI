using System.Runtime.CompilerServices;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.VectorData;

namespace ProcessDataAI.Ingestion;

/// <summary>
/// Writes ingestion chunks to a vector store while counting the chunks written.
/// </summary>
public sealed class CountingVectorStoreWriter(
    VectorStore vectorStore,
    int dimensionCount,
    VectorStoreWriterOptions options) : IngestionChunkWriter<string>
{
    private readonly VectorStoreWriter<string> _inner = new(vectorStore, dimensionCount, options);

    /// <summary>Gets the number of chunks written during ingestion.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>Gets the underlying vector collection used for search.</summary>
    public VectorStoreCollection<object, Dictionary<string, object?>> VectorStoreCollection =>
        _inner.VectorStoreCollection;

    /// <summary>
    /// Writes chunks to the configured vector collection and increments <see cref="ChunkCount"/>.
    /// </summary>
    /// <param name="chunks">The chunks to write.</param>
    /// <param name="cancellationToken">A token used to cancel the write.</param>
    /// <returns>A task that completes when the chunks have been written.</returns>
    public override Task WriteAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(CountAsync(chunks, cancellationToken), cancellationToken);

    private async IAsyncEnumerable<IngestionChunk<string>> CountAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (IngestionChunk<string> chunk in chunks.WithCancellation(cancellationToken))
        {
            ChunkCount++;
            yield return chunk;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
