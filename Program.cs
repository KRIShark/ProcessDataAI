using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using ProcessDataAI.Configuration;
using ProcessDataAI.Ingestion;
using ProcessDataAI.Mcp;
using ProcessDataAI.Services;
using ProcessDataAI.Testing;
using System.Security.Cryptography.X509Certificates;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        string contentRoot = Directory.GetCurrentDirectory();
        IReadOnlyDictionary<string, string?> envValues = EnvFile.Load(Path.Combine(contentRoot, ".env"));

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddInMemoryCollection(envValues);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        Uri mcpServerUri = GetHttpsServerUri(builder.Configuration["MCP_URL"]);
        bool runSmokeTest = HasArgument(args, "--mcp-smoke-test");
        if (runSmokeTest)
        {
            builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Debug);
        }
        using X509Certificate2? smokeTestCertificate = runSmokeTest
            ? LoopbackCertificate.Create()
            : null;
        if (smokeTestCertificate is null)
        {
            builder.WebHost.UseUrls(mcpServerUri.AbsoluteUri.TrimEnd('/'));
        }
        else
        {
            builder.WebHost.ConfigureKestrel(options =>
                options.ListenLocalhost(
                    mcpServerUri.Port,
                    listenOptions => listenOptions.UseHttps(smokeTestCertificate)));
        }
        builder.Configuration["AllowedHosts"] =
            builder.Configuration["MCP_ALLOWED_HOSTS"] ?? "localhost;127.0.0.1;[::1]";
        Uri publicBaseUri = GetHttpsPublicBaseUri(
            builder.Configuration["MCP_PUBLIC_BASE_URL"],
            mcpServerUri);

        builder.Services
            .AddOptions<AiOptions>()
            .Configure(options =>
            {
                options.Provider = builder.Configuration["AI_PROVIDER"] ?? string.Empty;
                options.Azure.Endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"] ?? string.Empty;
                options.Azure.ApiKey = builder.Configuration["AZURE_OPENAI_API_KEY"] ?? string.Empty;
                options.Azure.EmbeddingModel = builder.Configuration["AZURE_OPENAI_EMBEDDING_MODEL"] ?? string.Empty;
                options.Azure.ChatModel = builder.Configuration["AZURE_OPENAI_CHAT_MODEL"] ?? string.Empty;
                options.OpenAI.Endpoint = builder.Configuration["OPENAI_ENDPOINT"] ?? string.Empty;
                options.OpenAI.ApiKey = builder.Configuration["OPENAI_API_KEY"] ?? string.Empty;
                options.OpenAI.EmbeddingModel = builder.Configuration["OPENAI_EMBEDDING_MODEL"] ?? string.Empty;
                options.OpenAI.ChatModel = builder.Configuration["OPENAI_CHAT_MODEL"] ?? string.Empty;
                options.Ollama.Endpoint = builder.Configuration["OLLAMA_ENDPOINT"] ?? string.Empty;
                options.Ollama.EmbeddingModel = builder.Configuration["OLLAMA_EMBEDDING_MODEL"] ?? string.Empty;
                options.Ollama.ChatModel = builder.Configuration["OLLAMA_CHAT_MODEL"] ?? string.Empty;
            })
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
        builder.Services
            .AddOptions<RagMcpOptions>()
            .Configure(options =>
            {
                options.PublicBaseUrl = publicBaseUri.AbsoluteUri;
            });
        builder.Services.AddSingleton<PdfPigDocumentReader>();
        builder.Services.AddSingleton<DocumentCatalog>();
        builder.Services.AddSingleton<EmbeddingGeneratorFactory>();
        builder.Services.AddSingleton<ChatClientFactory>();
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            services => services.GetRequiredService<EmbeddingGeneratorFactory>().Create());
        builder.Services.AddSingleton<IChatClient>(
            services => new ConsoleLoggingChatClient(
                services.GetRequiredService<ChatClientFactory>().Create(),
                services.GetRequiredService<ILogger<ConsoleLoggingChatClient>>()));
        builder.Services.AddSingleton<DocumentSearchService>();
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithTools<RagMcpTools>();

        await using WebApplication app = builder.Build();
        app.MapMcp("/mcp");
        app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        app.MapGet("/documents/{id}", (string id, DocumentCatalog catalog) =>
            catalog.TryGetById(id, out CatalogDocument document)
                ? Results.Text(document.Text, "text/plain; charset=utf-8")
                : Results.NotFound());

        var searchService = app.Services.GetRequiredService<DocumentSearchService>();
        string configuredDataDirectory = builder.Configuration["DATA_DIRECTORY"] ?? "Data";
        if (string.IsNullOrWhiteSpace(configuredDataDirectory))
        {
            configuredDataDirectory = "Data";
        }

        string dataDirectory = Path.GetFullPath(configuredDataDirectory, contentRoot);

        if (runSmokeTest)
        {
            await app.StartAsync();
            try
            {
                await McpSmokeTest.AssertHttpsAsync(
                    GetServerBaseUri(mcpServerUri),
                    CancellationToken.None);
                await searchService.IngestAsync(dataDirectory, CancellationToken.None);
                await McpSmokeTest.RunAsync(GetServerBaseUri(mcpServerUri), CancellationToken.None);
                return 0;
            }
            finally
            {
                await app.StopAsync();
            }
        }

        await searchService.IngestAsync(dataDirectory, CancellationToken.None);

        string? oneShotQuery = GetQueryArgument(args);
        if (!string.IsNullOrWhiteSpace(oneShotQuery))
        {
            await searchService.SearchAsync(oneShotQuery, CancellationToken.None);
            return 0;
        }

        app.Logger.LogInformation(
            "MCP Streamable HTTP endpoint: {McpEndpoint}",
            new Uri(GetServerBaseUri(mcpServerUri), "mcp"));
        await app.RunAsync();
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(HasArgument(args, "--mcp-smoke-test")
            ? $"ERROR: {exception}"
            : $"ERROR: {exception.Message}");
        return 1;
    }
}

static bool HasArgument(string[] args, string name) =>
    args.Any(argument => argument.Equals(name, StringComparison.OrdinalIgnoreCase));

static Uri GetHttpsServerUri(string? configuredUrl)
{
    string value = string.IsNullOrWhiteSpace(configuredUrl)
        ? "https://localhost:7443"
        : configuredUrl;
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
        uri.Scheme != Uri.UriSchemeHttps ||
        uri.AbsolutePath != "/" ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException(
            "MCP_URL must be an absolute HTTPS origin without a path, query, or fragment.");
    }

    return uri;
}

static Uri GetServerBaseUri(Uri serverUri)
{
    var builder = new UriBuilder(serverUri)
    {
        Path = "/",
        Query = string.Empty,
        Fragment = string.Empty,
    };
    return builder.Uri;
}

static Uri GetHttpsPublicBaseUri(string? configuredUrl, Uri mcpServerUri)
{
    if (string.IsNullOrWhiteSpace(configuredUrl))
    {
        return GetServerBaseUri(mcpServerUri);
    }

    if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri? uri) ||
        uri.Scheme != Uri.UriSchemeHttps ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException(
            "MCP_PUBLIC_BASE_URL must be an absolute HTTPS URL without a query or fragment.");
    }

    return uri;
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
