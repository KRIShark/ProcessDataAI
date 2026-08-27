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

/// <summary>
/// Ingests PDF files into an in-memory vector store and performs semantic document searches.
/// </summary>
public sealed class DocumentSearchService(
    PdfPigDocumentReader reader,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient,
    DocumentCatalog documentCatalog,
    ILoggerFactory loggerFactory,
    ILogger<DocumentSearchService> logger) : IDisposable
{
    private InMemoryVectorStore? _vectorStore;
    private CountingVectorStoreWriter? _writer;
    private IngestionPipeline<string>? _pipeline;
    private bool _isIngested;

    /// <summary>
    /// Ingests all top-level PDF files in a directory and makes them available for semantic search.
    /// </summary>
    /// <param name="dataDirectory">The directory containing PDF files.</param>
    /// <param name="cancellationToken">A token used to cancel ingestion.</param>
    /// <returns>A task that completes after ingestion.</returns>
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

        await documentCatalog.RegisterSourcesAsync(pdfs, cancellationToken);

        logger.LogInformation("Discovered {PdfCount} PDF file(s) in the configured data directory", pdfs.Length);
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
        IngestionChunker<string> chunker = new SemanticSimilarityChunker(embeddingGenerator, chunkerOptions);

        IngestionDocumentProcessor imageAlternativeTextEnricher =
            new ImageAlternativeTextProcessor(
                chatClient,
                loggerFactory.CreateLogger<ImageAlternativeTextProcessor>());

        _pipeline = new IngestionPipeline<string>(
            reader,
            chunker,
            _writer,
            loggerFactory: loggerFactory);
        _pipeline.DocumentProcessors.Add(imageAlternativeTextEnricher);
        _pipeline.DocumentProcessors.Add(new DocumentCatalogProcessor(documentCatalog));

        int successfulDocuments = 0;
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Generating embeddings and writing chunks to the in-memory vector store");

        await foreach (IngestionResult result in _pipeline.ProcessAsync(pdfs, cancellationToken))
        {
            if (!result.Succeeded)
            {
                logger.LogError(
                    "Failed to ingest PDF {DocumentName}; error type: {ErrorType}",
                    Path.GetFileName(result.DocumentId),
                    result.Exception?.GetType().Name ?? "unknown");
                continue;
            }

            successfulDocuments++;
            logger.LogInformation("Ingested PDF {DocumentName}", Path.GetFileName(result.DocumentId));
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

    /// <summary>
    /// Executes a semantic search and writes the highest-scoring chunks to the console.
    /// </summary>
    /// <param name="query">The query to search for.</param>
    /// <param name="cancellationToken">A token used to cancel the search.</param>
    /// <returns>A task that completes after results have been written.</returns>
    public async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!_isIngested || _writer is null)
        {
            throw new InvalidOperationException("Documents must be ingested before searching.");
        }

        logger.LogInformation("Executing semantic search for a query of {QueryCharacterCount} character(s)", query.Length);
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

    /// <summary>
    /// Searches indexed chunks and returns distinct matching documents, capped at the requested count.
    /// </summary>
    /// <param name="query">The query to search for.</param>
    /// <param name="maxResults">The maximum number of distinct documents to return.</param>
    /// <param name="cancellationToken">A token used to cancel the search.</param>
    /// <returns>Matching documents with stable IDs, titles, previews, and source metadata.</returns>
    public async Task<IReadOnlyList<DocumentSearchMatch>> SearchDocumentsAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (!_isIngested || _writer is null)
        {
            throw new InvalidOperationException("Documents must be ingested before searching.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);

        var matches = new List<DocumentSearchMatch>(maxResults);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int candidateCount = Math.Clamp(maxResults * 4, maxResults, 50);
        await foreach (VectorSearchResult<Dictionary<string, object?>> result in
            _writer.VectorStoreCollection.SearchAsync(
                query,
                top: candidateCount,
                cancellationToken: cancellationToken))
        {
            string? identifier = GetString(
                result.Record,
                "documentid",
                "document_id",
                "documentId",
                "DocumentId");
            if (identifier is null ||
                !documentCatalog.TryGetByIdentifier(identifier, out CatalogDocument document) ||
                !seenIds.Add(document.Id))
            {
                continue;
            }

            string? content = GetString(result.Record, "content", "Content");
            matches.Add(new DocumentSearchMatch(
                document.Id,
                document.Title,
                CreateSnippet(content),
                document.SizeBytes,
                document.MediaType));
            if (matches.Count == maxResults)
            {
                break;
            }
        }

        return matches;
    }

    private static string? CreateSnippet(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        const int maxLength = 500;
        string snippet = content.ReplaceLineEndings(" ").Trim();
        return snippet.Length <= maxLength
            ? snippet
            : $"{snippet[..(maxLength - 3)]}...";
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

    /// <summary>
    /// Disposes the ingestion pipeline and in-memory vector store.
    /// </summary>
    public void Dispose()
    {
        _pipeline?.Dispose();
        _vectorStore?.Dispose();
    }
}

/// <summary>
/// Represents a distinct document and preview returned by semantic search.
/// </summary>
public sealed record DocumentSearchMatch(
    string Id,
    string Title,
    string? Text,
    long SizeBytes,
    string MediaType);
