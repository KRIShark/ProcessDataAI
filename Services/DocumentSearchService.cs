using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel.Connectors.InMemory;
using ProcessDataAI.Ingestion;

namespace ProcessDataAI.Services;

public sealed class DocumentSearchService(
    PdfPigDocumentReader reader,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILoggerFactory loggerFactory,
    ILogger<DocumentSearchService> logger) : IDisposable
{
    private InMemoryVectorStore? _vectorStore;
    private CountingVectorStoreWriter? _writer;
    private IngestionPipeline<string>? _pipeline;
    private bool _isIngested;

    public async Task IngestAsync(string dataDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dataDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Data directory '{dataDirectory}' was not found. Create it and add at least one PDF.");
        }

        FileInfo[] pdfs = new DirectoryInfo(dataDirectory)
            .GetFiles("*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (pdfs.Length == 0)
        {
            throw new InvalidOperationException($"Data directory '{dataDirectory}' contains no PDF files.");
        }

        logger.LogInformation("Discovered {PdfCount} PDF file(s) in {DataDirectory}", pdfs.Length, dataDirectory);
        logger.LogInformation("Generating an embedding to determine the provider vector dimensions");

        ReadOnlyMemory<float> probe;
        try //How large the embedings dimensions are
        {
            probe = await embeddingGenerator.GenerateVectorAsync(
                "embedding dimension probe",
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Embedding generation failed. Verify the selected provider, endpoint, model, and network access. {exception.Message}",
                exception);
        }

        _vectorStore = new InMemoryVectorStore(new() { EmbeddingGenerator = embeddingGenerator });
        _writer = new CountingVectorStoreWriter(
            _vectorStore,
            probe.Length,
            new VectorStoreWriterOptions
            {
                CollectionName = "documents",
                DistanceFunction = DistanceFunction.CosineSimilarity
            });

        var chunkerOptions = new IngestionChunkerOptions(TiktokenTokenizer.CreateForModel("gpt-4o"))
        {
            MaxTokensPerChunk = 500,
            OverlapTokens = 75
        };
        IngestionChunker<string> chunker = new DocumentTokenChunker(chunkerOptions);

        _pipeline = new IngestionPipeline<string>(
            reader,
            chunker,
            _writer,
            loggerFactory: loggerFactory);

        int successfulDocuments = 0;
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Generating embeddings and writing chunks to the in-memory vector store");

        await foreach (IngestionResult result in _pipeline.ProcessAsync(pdfs, cancellationToken))
        {
            if (!result.Succeeded)
            {
                logger.LogError(result.Exception, "Failed to ingest PDF {DocumentName}", result.DocumentId);
                continue;
            }

            successfulDocuments++;
            logger.LogInformation("Ingested PDF {DocumentName}", result.DocumentId);
        }

        stopwatch.Stop();
        if (successfulDocuments == 0)
        {
            throw new InvalidOperationException("No PDFs could be ingested. Review the preceding PDF error messages.");
        }

        _isIngested = true;
        logger.LogInformation(
            "Ingestion completed: {DocumentCount}/{PdfCount} document(s), {ChunkCount} chunk(s), {ElapsedMilliseconds} ms",
            successfulDocuments,
            pdfs.Length,
            _writer.ChunkCount,
            stopwatch.ElapsedMilliseconds);
    }

    public async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!_isIngested || _writer is null)
        {
            throw new InvalidOperationException("Documents must be ingested before searching.");
        }

        logger.LogInformation("Executing semantic search for: {Query}", query);
        try
        {
            await foreach (VectorSearchResult<Dictionary<string, object?>> result in
                _writer.VectorStoreCollection.SearchAsync(query, top: 3, cancellationToken: cancellationToken))
            {
                string documentId = GetString(result.Record, "documentid", "document_id", "documentId", "DocumentId") ?? "unknown";
                string documentName = Path.GetFileName(documentId);
                string content = GetString(result.Record, "content", "Content") ?? string.Empty;
                Console.WriteLine($"Score: {result.Score:F4}");
                Console.WriteLine($"Document: {documentName}");
                Console.WriteLine($"Content: {content}");
                Console.WriteLine();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Semantic search failed: {exception.Message}", exception);
        }
    }

    private static string? GetString(Dictionary<string, object?> record, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (record.TryGetValue(key, out object? value))
            {
                return value?.ToString();
            }
        }

        return null;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _vectorStore?.Dispose();
    }
}
