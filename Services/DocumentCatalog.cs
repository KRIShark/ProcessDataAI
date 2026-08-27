using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DataIngestion;

namespace ProcessDataAI.Services;

public sealed class DocumentCatalog
{
    private readonly ConcurrentDictionary<string, CatalogDocument> _documents =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _idsByIdentifier =
        new(StringComparer.OrdinalIgnoreCase);

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

    public bool TryGetById(string id, out CatalogDocument document) =>
        _documents.TryGetValue(id, out document!);
}

public sealed record CatalogDocument(
    string Id,
    string Title,
    string Identifier,
    string Text,
    long SizeBytes,
    string MediaType);
