using System.Text.Json;
using CodeKb.Contracts;
using CodeKb.Core;
using CodeKb.Core.Configuration;
using CodeKb.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodeKb.Cli.Commands;

public static class AskCommand
{
    public static async Task<int> RunAsync(SearchRequest req, string? configPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question))
        {
            Console.Error.WriteLine("question argument is required");
            return CliRoot.ExitUserError;
        }

        CodeKbOptions options;
        try { options = ConfigLoader.Load(configPath); }
        catch (ConfigLoadException ex)
        {
            Console.Error.WriteLine($"config error: {ex.Message}");
            return CliRoot.ExitUserError;
        }

        var services = new ServiceCollection().AddCodeKb(options).BuildServiceProvider();
        try
        {
            var svc = services.GetRequiredService<ISearchService>();
            var hits = await svc.AskAsync(req, ct);
            PrintHits(req, hits);
            return CliRoot.ExitOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ask failed: {ex.Message}");
            return CliRoot.ExitInfraError;
        }
    }

    public static void PrintHits(SearchRequest req, IReadOnlyList<SearchHit> hits, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        if (string.Equals(req.Format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var payload = hits.Select(h => new
            {
                repository = h.Repository,
                branch = h.Branch,
                commit_sha = h.CommitSha,
                file_path = h.FilePath,
                line_start = h.LineStart,
                line_end = h.LineEnd,
                symbol_name = h.SymbolName,
                record_type = h.RecordType.ToWire(),
                summary = h.Summary,
                code_snippet = h.CodeSnippet,
                score = h.Score,
            });
            writer.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        writer.WriteLine($"Question: {req.Question}");
        int i = 1;
        foreach (var h in hits)
        {
            writer.WriteLine($"{i}. {h.Repository}");
            writer.WriteLine($"   File:   {h.FilePath}:{h.LineStart}-{h.LineEnd}");
            writer.WriteLine($"   Symbol: {h.SymbolName}");
            writer.WriteLine($"   Match:  semantic");
            writer.WriteLine($"   Score:  {h.Score:0.00}");
            i++;
        }
    }
}
