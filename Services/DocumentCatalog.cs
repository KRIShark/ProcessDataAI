using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DataIngestion;

namespace ProcessDataAI.Services;

/// <summary>
/// Maintains stable document IDs and fully processed document content for MCP retrieval.
/// </summary>
public sealed class DocumentCatalog
{
    private readonly ConcurrentDictionary<string, CatalogDocument> _documents =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _idsByIdentifier =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers source PDFs and assigns each one a stable ID derived from its content.
    /// </summary>
    /// <param name="files">The PDF files that will be ingested.</param>
    /// <param name="cancellationToken">A token used to cancel registration.</param>
    /// <returns>A task that completes once every source has been registered.</returns>
    public async Task RegisterSourcesAsync(
        IEnumerable<FileInfo> files,
        CancellationToken cancellationToken)
    {
        _documents.Clear();
        _idsByIdentifier.Clear();

        foreach (FileInfo file in files)
        {
            await using FileStream stream = file.OpenRead();
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            string id = $"doc-{Convert.ToHexStringLower(hash)}";
            string identifier = Path.GetFullPath(file.FullName);
            var document = new CatalogDocument(
                id,
                file.Name,
                identifier,
                string.Empty,
                file.Length,
                "application/pdf");

            _idsByIdentifier[identifier] = id;
            _documents[id] = document;
        }
    }

    /// <summary>
    /// Captures the full text of an already processed ingestion document.
    /// </summary>
    /// <param name="document">The processed document to capture.</param>
    public void Capture(IngestionDocument document)
    {
        string identifier = Path.GetFullPath(document.Identifier);
        if (!_idsByIdentifier.TryGetValue(identifier, out string? id) ||
            !_documents.TryGetValue(id, out CatalogDocument? registeredDocument))
        {
            return;
        }

        string text = string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            document.Sections
                .SelectMany(section => section.Elements)
                .Select(element => element.GetMarkdown())
                .Where(markdown => !string.IsNullOrWhiteSpace(markdown)));

        _documents[id] = registeredDocument with { Text = text };
    }

    /// <summary>
    /// Tries to find a catalog entry by its source file identifier.
    /// </summary>
    /// <param name="identifier">The source file identifier.</param>
    /// <param name="document">The matching catalog entry when found.</param>
    /// <returns><see langword="true"/> when a matching document exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGetByIdentifier(string identifier, out CatalogDocument document)
    {
        string fullIdentifier = Path.GetFullPath(identifier);
        if (_idsByIdentifier.TryGetValue(fullIdentifier, out string? id) &&
            _documents.TryGetValue(id, out CatalogDocument? found))
        {
            document = found;
            return true;
        }

        document = null!;
        return false;
    }

    /// <summary>
    /// Tries to find a catalog entry by its stable document ID.
    /// </summary>
    /// <param name="id">The stable document ID.</param>
    /// <param name="document">The matching catalog entry when found.</param>
    /// <returns><see langword="true"/> when a matching document exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGetById(string id, out CatalogDocument document) =>
        _documents.TryGetValue(id, out document!);
}

/// <summary>
/// Contains the identity, extracted content, and source metadata for a cataloged PDF.
/// </summary>
public sealed record CatalogDocument(
    string Id,
    string Title,
    string Identifier,
    string Text,
    long SizeBytes,
    string MediaType);
