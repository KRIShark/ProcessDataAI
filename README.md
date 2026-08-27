# ProcessDataAI

Minimal PDF ingestion and semantic search sample built on the Microsoft AI extensions stack.

## Configure

Copy `.env.example` to `.env`, set `AI_PROVIDER` to `Azure`, `OpenAI`, or `Ollama`, and fill in the settings for that provider. The unselected provider's values may remain empty. `.env` is ignored by Git.

Set `DATA_DIRECTORY` to the directory containing the PDFs. Absolute paths are used as-is; relative paths are resolved from the application content directory. It defaults to `Data` when omitted or empty.

For Azure OpenAI, the model values are deployment names. Set `AZURE_OPENAI_CHAT_MODEL` to a vision-capable chat deployment.

For OpenAI or another OpenAI-compatible service, use `AI_PROVIDER=OpenAI` and configure `OPENAI_ENDPOINT`, `OPENAI_API_KEY`, `OPENAI_EMBEDDING_MODEL`, and `OPENAI_CHAT_MODEL`. The endpoint must include the service's `/v1` API path. `OPENAI_API_KEY` may be empty for a local service that does not require authentication. The chat model must support image inputs.

For Ollama, set `OLLAMA_ENDPOINT` to either the server root (for example, `http://localhost:11434`) or its OpenAI-compatible `/v1` URL. Both configured models must already be available and `OLLAMA_CHAT_MODEL` must support images. A text-only model still works for text-bearing PDFs, but it cannot generate alternative text for embedded images.

## Run

Place PDFs in the configured `DATA_DIRECTORY`, then run:

```powershell
dotnet run
```

Enter questions at the prompt. To execute one query and exit:

```powershell
dotnet run -- --query "How many vacation days do employees receive?"
```

The application reads every top-level `*.pdf`, extracts text plus embedded PNG and JPEG images with PdfPig (without OCR), generates image alternative text one image at a time, chunks content with `SemanticSimilarityChunker`, generates embeddings, stores chunks in `InMemoryVectorStore`, and returns the top three semantic matches. Enrichment is best-effort, so a failure to describe an individual image is logged without failing the whole document.

During a run, the console logs each AI request and response used for enrichment. Binary image bytes are not printed; the log shows the MIME type, byte count, and whether the request contains an image. Prompts and model responses can contain document data, so use this POC logging only in a trusted environment.
