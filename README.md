# ProcessDataAI

PDF, Markdown, text, and image ingestion; semantic retrieval; and an HTTP/HTTPS Model Context Protocol server built with the Microsoft AI extensions stack and the official C# MCP SDK.

## Configure

Copy `.env.example` to `.env`, set `AI_PROVIDER` to `Azure`, `OpenAI`, or `Ollama`, and fill in the selected provider's settings. The unselected provider values may remain empty. `.env` is ignored by Git.

`DATA_DIRECTORY` selects the directory containing source files. Absolute paths are used as-is; relative paths are resolved from the application content directory. It defaults to `Data` when omitted or empty. Supported files are `.pdf`, `.md`, `.markdown`, `.txt`, `.png`, `.jpg`, `.jpeg`, `.gif`, and `.webp`.

The MCP server settings are:

```env
MCP_URL=https://localhost:7443
MCP_PUBLIC_BASE_URL=https://localhost:7443
MCP_ALLOWED_HOSTS=localhost;127.0.0.1;[::1]
MCP_ALLOW_HTTP=false
MCP_AUTH_TOKEN=
```

- `MCP_URL` is the origin Kestrel listens on. Paths are not allowed; the MCP route is always `/mcp`.
- `MCP_PUBLIC_BASE_URL` is the externally reachable origin used to create document citation URLs. Set this to the reverse-proxy or public origin when deploying remotely.
- `MCP_ALLOWED_HOSTS` is the semicolon-separated ASP.NET Core host allowlist. Add only the exact deployment host names.
- `MCP_ALLOW_HTTP` defaults to `false`. Set it to `true` to permit `http://` values for `MCP_URL` and `MCP_PUBLIC_BASE_URL`.

Kestrel needs an HTTPS certificate. For local development, create and trust the ASP.NET Core development certificate:

```powershell
dotnet dev-certs https --trust
```

For production, configure a real certificate through the standard ASP.NET Core Kestrel certificate settings or terminate TLS at a trusted reverse proxy. When `MCP_URL` or `MCP_PUBLIC_BASE_URL` is not loopback, the application requires `MCP_AUTH_TOKEN` and protects `/mcp` and `/documents/*` with a Bearer token. Keep the token in deployment-managed secret storage; never commit it.

For local HTTP development, which avoids development-certificate issues in tools such as MCP Inspector, use:

```env
MCP_ALLOW_HTTP=true
MCP_URL=http://localhost:7443
MCP_PUBLIC_BASE_URL=http://localhost:7443
```

HTTP is unencrypted. Keep it on loopback or a trusted private network; use HTTPS whenever traffic can cross an untrusted network.

For Azure OpenAI, configure `AZURE_OPENAI_EMBEDDING_ENDPOINT` and `AZURE_OPENAI_CHAT_ENDPOINT`; the two endpoints may differ. Model values are deployment names. Set `AZURE_OPENAI_CHAT_MODEL` to a vision-capable chat deployment.

For OpenAI or another OpenAI-compatible service, configure `OPENAI_EMBEDDING_ENDPOINT`, `OPENAI_CHAT_ENDPOINT`, `OPENAI_API_KEY`, `OPENAI_EMBEDDING_MODEL`, and `OPENAI_CHAT_MODEL`. The endpoints may be different and must include each service's `/v1` API path. `OPENAI_API_KEY` may be empty for a local service that does not require authentication. A vision-capable chat model is required to ingest standalone images or describe images embedded in PDFs.

For Ollama, configure `OLLAMA_EMBEDDING_ENDPOINT` and `OLLAMA_CHAT_ENDPOINT`. Each may be a server root, such as `http://localhost:11434`, or an OpenAI-compatible `/v1` URL, and they may address different servers. Both configured models must already be available. A vision-capable chat model is required to ingest standalone images or describe images embedded in PDFs; text-only sources still work with a text-only model.

For backward compatibility, the legacy `AZURE_OPENAI_ENDPOINT`, `OPENAI_ENDPOINT`, and `OLLAMA_ENDPOINT` settings remain supported as fallbacks for both corresponding endpoints. A role-specific endpoint takes precedence when it is set.

## Run the MCP server

Place supported files in `DATA_DIRECTORY`, then run:

```powershell
dotnet run
```

After ingestion completes, connect a Streamable HTTP MCP client to:

```text
https://localhost:7443/mcp
```

This is the current Streamable HTTP transport. Legacy SSE routes such as `/mcp/sse` and `/mcp/message` are not enabled.

The server exposes two read-only tools compatible with OpenAI search/fetch integrations:

- `search(query)` performs dense semantic vector retrieval and returns `results`. Every result contains a stable document `id` and source filename as `title`; it may also contain a citation `url`, a short relevant `text` preview, and `metadata`.
- `fetch(id)` retrieves the required `id`, filename `title`, and complete extracted document `text`. A citation `url` and source `metadata` are optional. The ID is a stable `doc-` prefixed SHA-256 hash of the source file bytes.

Both tools advertise output schemas and return the result in MCP `structuredContent` plus JSON compatibility text. Search results are deduplicated by document. Retrieval is dense vector search with cosine similarity; it is not keyword or hybrid retrieval.

The citation URL is also available as `GET /documents/{id}` and returns the extracted document as UTF-8 plain text. `GET /health` provides a readiness endpoint.

To run one console search and exit without starting the MCP server:

```powershell
dotnet run -- --query "How many vacation days do employees receive?"
```

## End-to-end smoke test

With a configured `.env`, run:

```powershell
dotnet run -- --mcp-smoke-test
```

The smoke test starts the configured HTTP or HTTPS server, ingests the configured source files, connects with the official C# MCP Streamable HTTP client, lists the tools, calls `search`, calls `fetch` with the returned ID, downloads the citation URL, and verifies that the legacy SSE endpoint is absent. For HTTPS, it creates a temporary loopback certificate that is not trusted or installed and is disposed when the test finishes.

## Ingestion behavior

The application reads every supported top-level file in `DATA_DIRECTORY`; subdirectories are not searched. It preserves UTF-8 Markdown and plain text, extracts selectable text plus embedded PNG and JPEG images from PDFs with PdfPig, and loads standalone PNG, JPEG, GIF, and WebP images. PDF extraction does not perform OCR.

Images are sent one at a time to the configured chat model for alternative-text generation. The resulting text is chunked with `SemanticSimilarityChunker`, embedded, and stored in `InMemoryVectorStore`. Generated descriptions are included in fetched content as `Image: <description>` or `Image on page N: <description>`. Image enrichment is best-effort for documents that also contain text; a standalone image needs a successful description to produce searchable content.

During ingestion, the console logs only AI request/response metadata such as content counts, media types, byte counts, model ID, and text lengths. Prompts, model responses, image bytes, and document contents are not written to logs. One-shot console search intentionally prints retrieved content to the terminal; treat captured terminal output as sensitive.
