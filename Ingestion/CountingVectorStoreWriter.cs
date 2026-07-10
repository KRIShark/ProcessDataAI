using System.Runtime.CompilerServices;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.VectorData;

namespace ProcessDataAI.Ingestion;

public sealed class CountingVectorStoreWriter(
    VectorStore vectorStore,
    int dimensionCount,
    VectorStoreWriterOptions options) : IngestionChunkWriter<string>
{
    private readonly VectorStoreWriter<string> _inner = new(vectorStore, dimensionCount, options);

    public int ChunkCount { get; private set; }

    public VectorStoreCollection<object, Dictionary<string, object?>> VectorStoreCollection =>
        _inner.VectorStoreCollection;

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
