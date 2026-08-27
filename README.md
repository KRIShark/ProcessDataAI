# ProcessDataAI

PDF ingestion, semantic retrieval, and an HTTPS Model Context Protocol server built with the Microsoft AI extensions stack and the official C# MCP SDK.

## Configure

Copy `.env.example` to `.env`, set `AI_PROVIDER` to `Azure`, `OpenAI`, or `Ollama`, and fill in the selected provider's settings. The unselected provider values may remain empty. `.env` is ignored by Git.

`DATA_DIRECTORY` selects the directory containing PDFs. Absolute paths are used as-is; relative paths are resolved from the application content directory. It defaults to `Data` when omitted or empty.

The MCP server settings are:

```env
MCP_URL=https://localhost:7443
MCP_PUBLIC_BASE_URL=https://localhost:7443
MCP_ALLOWED_HOSTS=localhost;127.0.0.1;[::1]
```

- `MCP_URL` is the HTTPS origin Kestrel listens on. Paths are not allowed; the MCP route is always `/mcp`.
- `MCP_PUBLIC_BASE_URL` is the externally reachable HTTPS origin used to create document citation URLs. Set this to the reverse-proxy or public origin when deploying remotely.
- `MCP_ALLOWED_HOSTS` is the semicolon-separated ASP.NET Core host allowlist. Add only the exact deployment host names.

Kestrel needs an HTTPS certificate. For local development, create and trust the ASP.NET Core development certificate:

```powershell
dotnet dev-certs https --trust
```

For production, configure a real certificate through the standard ASP.NET Core Kestrel certificate settings or terminate TLS at a trusted reverse proxy. The MCP endpoint currently has no authentication, so do not expose private documents publicly without adding authentication and authorization.

For Azure OpenAI, model values are deployment names. Set `AZURE_OPENAI_CHAT_MODEL` to a vision-capable chat deployment.

For OpenAI or another OpenAI-compatible service, configure `OPENAI_ENDPOINT`, `OPENAI_API_KEY`, `OPENAI_EMBEDDING_MODEL`, and `OPENAI_CHAT_MODEL`. The endpoint must include the service's `/v1` API path. `OPENAI_API_KEY` may be empty for a local service that does not require authentication. The chat model must support image inputs.

For Ollama, `OLLAMA_ENDPOINT` may be the server root, such as `http://localhost:11434`, or its OpenAI-compatible `/v1` URL. Both configured models must already be available. A vision-capable chat model is needed to describe embedded images; text-bearing PDFs still work with a text-only model.

## Run the MCP server

Place PDFs in `DATA_DIRECTORY`, then run:

```powershell
dotnet run
```

After ingestion completes, connect a Streamable HTTP MCP client to:

```text
https://localhost:7443/mcp
```

This is the current Streamable HTTP transport. Legacy SSE routes such as `/mcp/sse` and `/mcp/message` are not enabled.

The server exposes two read-only tools compatible with OpenAI search/fetch integrations:

- `search(query)` performs dense semantic vector retrieval and returns `results`, containing a stable document `id`, the PDF filename as `title`, and an HTTPS citation `url`.
- `fetch(id)` retrieves the complete extracted document text, filename, citation URL, and metadata. The ID is a stable `doc-` prefixed SHA-256 hash of the PDF bytes.

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

The smoke test creates a temporary loopback certificate, starts the HTTPS server, ingests the configured PDFs, connects with the official C# MCP Streamable HTTP client, lists the tools, calls `search`, calls `fetch` with the returned ID, downloads the citation URL over HTTPS, and verifies that the legacy SSE endpoint is absent. The temporary certificate is not trusted or installed and is disposed when the test finishes.

## Ingestion behavior

The application reads every top-level `*.pdf`, extracts text plus embedded PNG and JPEG images with PdfPig (without OCR), generates image alternative text one image at a time, chunks content with `SemanticSimilarityChunker`, creates embeddings, and stores chunks in `InMemoryVectorStore`. Image enrichment is best-effort, so a failure to describe an image does not prevent the remaining document text from being ingested.

During ingestion, the console logs AI requests and responses used for image enrichment. Binary image bytes are not printed; logs show their MIME type and byte count. Prompts and model responses can contain document data, so use this POC logging only in a trusted environment.
