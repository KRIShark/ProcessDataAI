# ProcessDataAI

Minimal PDF ingestion and semantic search sample built on the Microsoft AI extensions stack.

## Configure

Copy `.env.example` to `.env`, set `AI_PROVIDER` to `Azure` or `Ollama`, and fill in the settings for that provider. The unselected provider's values may remain empty. `.env` is ignored by Git.

For Azure OpenAI, `AZURE_OPENAI_EMBEDDING_MODEL` is the embedding deployment name. For Ollama, the endpoint must expose the OpenAI-compatible `/v1` API and the configured embedding model must already be available.

## Run

Place text-bearing PDFs in `Data/`, then run:

```powershell
dotnet run
```

Enter questions at the prompt. To execute one query and exit:

```powershell
dotnet run -- --query "How many vacation days do employees receive?"
```

The application reads every top-level `*.pdf`, extracts text with PdfPig (without OCR), chunks it with `DocumentTokenChunker`, generates embeddings, stores chunks in `InMemoryVectorStore`, and returns the top three semantic matches.
