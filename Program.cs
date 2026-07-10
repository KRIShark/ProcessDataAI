using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcessDataAI.Configuration;
using ProcessDataAI.Ingestion;
using ProcessDataAI.Services;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        string contentRoot = Directory.GetCurrentDirectory();
        IReadOnlyDictionary<string, string?> envValues = EnvFile.Load(Path.Combine(contentRoot, ".env"));

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddInMemoryCollection(envValues);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services
            .AddOptions<AiOptions>()
            .Configure(options =>
            {
                options.Provider = builder.Configuration["AI_PROVIDER"] ?? string.Empty;
                options.Azure.Endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"] ?? string.Empty;
                options.Azure.ApiKey = builder.Configuration["AZURE_OPENAI_API_KEY"] ?? string.Empty;
                options.Azure.EmbeddingModel = builder.Configuration["AZURE_OPENAI_EMBEDDING_MODEL"] ?? string.Empty;
                options.Ollama.Endpoint = builder.Configuration["OLLAMA_ENDPOINT"] ?? string.Empty;
                options.Ollama.EmbeddingModel = builder.Configuration["OLLAMA_EMBEDDING_MODEL"] ?? string.Empty;
            })
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
        builder.Services.AddSingleton<PdfPigDocumentReader>();
        builder.Services.AddSingleton<EmbeddingGeneratorFactory>();
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            services => services.GetRequiredService<EmbeddingGeneratorFactory>().Create());
        builder.Services.AddSingleton<DocumentSearchService>();

        using IHost host = builder.Build();
        await host.StartAsync();

        var searchService = host.Services.GetRequiredService<DocumentSearchService>();
        await searchService.IngestAsync(Path.Combine(contentRoot, "Data"), CancellationToken.None);

        string? oneShotQuery = GetQueryArgument(args);
        if (!string.IsNullOrWhiteSpace(oneShotQuery))
        {
            await searchService.SearchAsync(oneShotQuery, CancellationToken.None);
        }
        else if (!Console.IsInputRedirected)
        {
            while (true)
            {
                Console.Write("Question (or 'exit'): ");
                string? query = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await searchService.SearchAsync(query, CancellationToken.None);
            }
        }

        await host.StopAsync();
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"ERROR: {exception.Message}");
        return 1;
    }
}

static string? GetQueryArgument(string[] args)
{
    for (int index = 0; index < args.Length - 1; index++)
    {
        if (args[index].Equals("--query", StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}
