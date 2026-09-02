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
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        string contentRoot = Directory.GetCurrentDirectory();
        IReadOnlyDictionary<string, string?> envValues = EnvFile.Load(Path.Combine(contentRoot, ".env"));

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Configuration
            .AddInMemoryCollection(envValues)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        bool allowHttp = GetBooleanSetting(builder.Configuration, "MCP_ALLOW_HTTP", defaultValue: false);
        Uri mcpServerUri = GetServerUri(builder.Configuration["MCP_URL"], allowHttp);
        bool runSmokeTest = HasArgument(args, "--mcp-smoke-test");
        if (runSmokeTest)
        {
            builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Debug);
        }
        using X509Certificate2? smokeTestCertificate = runSmokeTest &&
            mcpServerUri.Scheme == Uri.UriSchemeHttps
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
        Uri publicBaseUri = GetPublicBaseUri(
            builder.Configuration["MCP_PUBLIC_BASE_URL"],
            mcpServerUri,
            allowHttp);
        string? authToken = builder.Configuration["MCP_AUTH_TOKEN"];
        bool requireAuthentication = RequiresAuthentication(mcpServerUri, publicBaseUri);
        if (requireAuthentication && string.IsNullOrWhiteSpace(authToken))
        {
            throw new InvalidOperationException(
                "MCP_AUTH_TOKEN is required when MCP_URL or MCP_PUBLIC_BASE_URL is not loopback.");
        }

        builder.Services
            .AddOptions<AiOptions>()
            .Configure(options =>
            {
                options.Provider = builder.Configuration["AI_PROVIDER"] ?? string.Empty;
                string azureEndpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"] ?? string.Empty;
                options.Azure.EmbeddingEndpoint =
                    builder.Configuration["AZURE_OPENAI_EMBEDDING_ENDPOINT"] ?? azureEndpoint;
                options.Azure.ChatEndpoint =
                    builder.Configuration["AZURE_OPENAI_CHAT_ENDPOINT"] ?? azureEndpoint;
                options.Azure.ApiKey = builder.Configuration["AZURE_OPENAI_API_KEY"] ?? string.Empty;
                options.Azure.EmbeddingModel = builder.Configuration["AZURE_OPENAI_EMBEDDING_MODEL"] ?? string.Empty;
                options.Azure.ChatModel = builder.Configuration["AZURE_OPENAI_CHAT_MODEL"] ?? string.Empty;
                string openAiEndpoint = builder.Configuration["OPENAI_ENDPOINT"] ?? string.Empty;
                options.OpenAI.EmbeddingEndpoint =
                    builder.Configuration["OPENAI_EMBEDDING_ENDPOINT"] ?? openAiEndpoint;
                options.OpenAI.ChatEndpoint =
                    builder.Configuration["OPENAI_CHAT_ENDPOINT"] ?? openAiEndpoint;
                options.OpenAI.ApiKey = builder.Configuration["OPENAI_API_KEY"] ?? string.Empty;
                options.OpenAI.EmbeddingModel = builder.Configuration["OPENAI_EMBEDDING_MODEL"] ?? string.Empty;
                options.OpenAI.ChatModel = builder.Configuration["OPENAI_CHAT_MODEL"] ?? string.Empty;
                string ollamaEndpoint = builder.Configuration["OLLAMA_ENDPOINT"] ?? string.Empty;
                options.Ollama.EmbeddingEndpoint =
                    builder.Configuration["OLLAMA_EMBEDDING_ENDPOINT"] ?? ollamaEndpoint;
                options.Ollama.ChatEndpoint =
                    builder.Configuration["OLLAMA_CHAT_ENDPOINT"] ?? ollamaEndpoint;
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
        app.Use(async (context, next) =>
        {
            bool protectsDocumentData =
                context.Request.Path.StartsWithSegments("/mcp") ||
                context.Request.Path.StartsWithSegments("/documents");
            if (requireAuthentication && protectsDocumentData &&
                !HasValidBearerToken(context.Request, authToken!))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }

            await next();
        });
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
                await McpSmokeTest.AssertServerAsync(
                    GetServerBaseUri(mcpServerUri),
                    CancellationToken.None);
                await searchService.IngestAsync(dataDirectory, CancellationToken.None);
                string imageDocumentPath = Path.Combine(dataDirectory, "stormworks.pdf");
                var catalog = app.Services.GetRequiredService<DocumentCatalog>();
                string? imageDocumentId = File.Exists(imageDocumentPath) &&
                    catalog.TryGetByIdentifier(imageDocumentPath, out CatalogDocument imageDocument)
                        ? imageDocument.Id
                        : null;
                await McpSmokeTest.RunAsync(
                    GetServerBaseUri(mcpServerUri),
                    imageDocumentId,
                    CancellationToken.None);
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

static Uri GetServerUri(string? configuredUrl, bool allowHttp)
{
    string value = string.IsNullOrWhiteSpace(configuredUrl)
        ? "https://localhost:7443"
        : configuredUrl;
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
        !IsAllowedMcpScheme(uri, allowHttp) ||
        uri.AbsolutePath != "/" ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException(
            allowHttp
                ? "MCP_URL must be an absolute HTTP or HTTPS origin without a path, query, or fragment."
                : "MCP_URL must be an absolute HTTPS origin without a path, query, or fragment. Set MCP_ALLOW_HTTP=true to permit HTTP.");
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

static Uri GetPublicBaseUri(string? configuredUrl, Uri mcpServerUri, bool allowHttp)
{
    if (string.IsNullOrWhiteSpace(configuredUrl))
    {
        return GetServerBaseUri(mcpServerUri);
    }

    if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri? uri) ||
        !IsAllowedMcpScheme(uri, allowHttp) ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException(
            allowHttp
                ? "MCP_PUBLIC_BASE_URL must be an absolute HTTP or HTTPS URL without a query or fragment."
                : "MCP_PUBLIC_BASE_URL must be an absolute HTTPS URL without a query or fragment. Set MCP_ALLOW_HTTP=true to permit HTTP.");
    }

    return uri;
}

static bool IsAllowedMcpScheme(Uri uri, bool allowHttp) =>
    uri.Scheme == Uri.UriSchemeHttps ||
    (allowHttp && uri.Scheme == Uri.UriSchemeHttp);

static bool RequiresAuthentication(Uri serverUri, Uri publicBaseUri) =>
    !IsLoopbackHost(serverUri.Host) || !IsLoopbackHost(publicBaseUri.Host);

static bool IsLoopbackHost(string host)
{
    if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? address) &&
        IPAddress.IsLoopback(address);
}

static bool HasValidBearerToken(HttpRequest request, string expectedToken)
{
    const string bearerPrefix = "Bearer ";
    string authorization = request.Headers.Authorization.ToString();
    if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    byte[] provided = Encoding.UTF8.GetBytes(authorization[bearerPrefix.Length..].Trim());
    byte[] expected = Encoding.UTF8.GetBytes(expectedToken);
    return CryptographicOperations.FixedTimeEquals(provided, expected);
}

static bool GetBooleanSetting(IConfiguration configuration, string name, bool defaultValue)
{
    string? value = configuration[name];
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    if (bool.TryParse(value, out bool result))
    {
        return result;
    }

    throw new InvalidOperationException($"{name} must be either 'true' or 'false'.");
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
