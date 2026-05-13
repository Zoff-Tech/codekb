using CodeKb.Contracts;
using CodeKb.Core;
using CodeKb.Core.Configuration;
using CodeKb.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodeKb.Cli.Commands;

public static class ScanCommand
{
    public static async Task<int> RunAsync(ScanRequest req, string? configPath, CancellationToken ct)
    {
        try { req.Validate(); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
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
            var svc = services.GetRequiredService<IScanService>();
            var result = await svc.ScanAsync(req, ct);
            PrintSummary(result);
            return CliRoot.ExitOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"scan failed: {ex.Message}");
            return CliRoot.ExitInfraError;
        }
    }

    public static void PrintSummary(ScanResult result, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        if (result.AlreadyIndexed)
        {
            writer.WriteLine($"No-op: {result.RepositoryName}@{result.Branch} commit {Trim(result.CommitSha)} already indexed.");
            return;
        }
        writer.WriteLine("Scan completed.");
        writer.WriteLine($"Repository:           {result.RepositoryName}");
        writer.WriteLine($"Branch:               {result.Branch}");
        writer.WriteLine($"Commit:               {Trim(result.CommitSha)}");
        writer.WriteLine($"Files scanned:        {result.Outcome.FilesScanned}");
        writer.WriteLine($"Records created:      {result.Outcome.RecordsCreated}");
        writer.WriteLine($"Feature flag matches: {result.Outcome.FeatureFlagMatches}");
        writer.WriteLine($"Embeddings created:   {result.Outcome.EmbeddingsCreated}");
        writer.WriteLine($"Duration:             {FormatDuration(result.Duration)}");
    }

    internal static string Trim(string sha) => sha.Length > 7 ? sha.Substring(0, 7) : sha;

    internal static string FormatDuration(TimeSpan d)
        => d.TotalMinutes >= 1
            ? $"{(int)d.TotalMinutes}m {d.Seconds:00}s"
            : $"{d.TotalSeconds:F1}s";
}
