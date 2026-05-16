using System.Net.Http;
using CodeKb.Core.Configuration;
using CodeKb.Core.Services;
using CodeKb.Embedding;
using CodeKb.Scanner.Roslyn;
using CodeKb.Scanner.Roslyn.Classification;
using CodeKb.Scanner.Roslyn.Detection;
using CodeKb.Scanner.Roslyn.Projects;
using CodeKb.Scanner.Roslyn.Redaction;
using CodeKb.Scanner.Roslyn.Syntax;
using CodeKb.Storage.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CodeKb.Core;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "DI wiring exercised by integration tests, not unit tests.")]
public static class CompositionRoot
{
    public static IServiceCollection AddCodeKb(this IServiceCollection services, CodeKbOptions options)
    {
        services.AddSingleton(options);

        services.AddSingleton<IFileClassifier, FileClassifier>();
        services.AddSingleton<ISyntaxRecordExtractor, SyntaxRecordExtractor>();
        services.AddSingleton<ISearchTermMatcher, SearchTermMatcher>();
        services.AddSingleton<IConfigFileScanner, ConfigFileScanner>();
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<IRedactor, Redactor>();
        services.AddSingleton<IRoslynScanner, RoslynScanner>();
        services.AddSingleton<IRepositoryLoader>(_ => new RepositoryLoader());

        if (!string.IsNullOrEmpty(options.Storage.PostgresConnectionString))
        {
            services.AddSingleton(_ => NpgsqlDataSourceFactory.Build(options.Storage.PostgresConnectionString));
            services.AddSingleton<IRepositoryStore>(sp => new PostgresRepositoryStore(sp.GetRequiredService<NpgsqlDataSource>()));
            services.AddSingleton<IScanJobStore>(sp => new PostgresScanJobStore(sp.GetRequiredService<NpgsqlDataSource>()));
            services.AddSingleton<ICodeRecordStore>(sp => new PostgresCodeRecordStore(sp.GetRequiredService<NpgsqlDataSource>()));
        }

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IEmbeddingClient>(sp =>
        {
            var http = sp.GetRequiredService<HttpClient>();
            return new OpenAIEmbeddingClient(
                http,
                options.Embedding.ApiKey ?? string.Empty,
                options.Embedding.Model,
                options.Embedding.Dimension,
                options.Embedding.Endpoint,
                options.Embedding.ModelVersion);
        });

        services.AddSingleton(sp => new RetryPolicy(options.Embedding.MaxRetries, options.Embedding.RetryBackoffSeconds));
        services.AddSingleton(sp => new EmbeddingPipeline(
            sp.GetRequiredService<IEmbeddingClient>(),
            sp.GetRequiredService<RetryPolicy>(),
            options.Embedding.BatchSize));

        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<ISearchService, SearchService>();

        services.AddLogging(b => b.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ ";
        }));

        return services;
    }
}
