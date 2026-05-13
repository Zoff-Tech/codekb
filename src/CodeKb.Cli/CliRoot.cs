using System.CommandLine;
using CodeKb.Cli.Commands;
using CodeKb.Contracts;

namespace CodeKb.Cli;

public static class CliRoot
{
    public const int ExitOk = 0;
    public const int ExitUserError = 2;
    public const int ExitInfraError = 3;
    public const int ExitPartialSuccess = 4;

    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("codekb — Roslyn-based code knowledge base");

        var configOption = new Option<string?>("--config", description: "Path to YAML config (default ./config/codekb.yaml)");

        root.AddCommand(BuildScanCommand(configOption));
        root.AddCommand(BuildAskCommand(configOption));

        return root;
    }

    public static Command BuildScanCommand(Option<string?> configOption)
    {
        var repoOpt = new Option<string?>("--repo", "Remote repository URL");
        var pathOpt = new Option<string?>("--path", "Local repository path");
        var branchOpt = new Option<string?>("--branch", "Branch to scan");
        var searchOpt = new Option<string[]>("--search", "Search term (repeatable)") { AllowMultipleArgumentsPerToken = true };
        var forceOpt = new Option<bool>("--force", "Re-scan even if commit is already indexed");

        var scan = new Command("scan", "Ingest a C# repository") { repoOpt, pathOpt, branchOpt, searchOpt, forceOpt, configOption };
        scan.SetHandler(async ctx =>
        {
            var req = new ScanRequest
            {
                RepoUrl = ctx.ParseResult.GetValueForOption(repoOpt),
                Path = ctx.ParseResult.GetValueForOption(pathOpt),
                Branch = ctx.ParseResult.GetValueForOption(branchOpt),
                SearchTerms = ctx.ParseResult.GetValueForOption(searchOpt) ?? Array.Empty<string>(),
                Force = ctx.ParseResult.GetValueForOption(forceOpt),
            };
            var configPath = ctx.ParseResult.GetValueForOption(configOption);
            ctx.ExitCode = await ScanCommand.RunAsync(req, configPath, ctx.GetCancellationToken());
        });
        return scan;
    }

    public static Command BuildAskCommand(Option<string?> configOption)
    {
        var questionArg = new Argument<string>("question", "Natural-language question");
        var repoOpt = new Option<string[]>("--repo", "Repository filter (repeatable)") { AllowMultipleArgumentsPerToken = true };
        var branchOpt = new Option<string?>("--branch", "Branch filter");
        var recordTypeOpt = new Option<string[]>("--record-type", "Record type filter (repeatable)") { AllowMultipleArgumentsPerToken = true };
        var featureFlagOpt = new Option<string?>("--feature-flag", "Filter by feature flag name");
        var topKOpt = new Option<int>("--top-k", () => 10, "Top-K results");
        var minScoreOpt = new Option<double?>("--min-score", "Minimum similarity score");
        var formatOpt = new Option<string>("--format", () => "text", "text|json");
        var includeStaleOpt = new Option<bool>("--include-stale", "Include stale records");
        var includeOtherModelsOpt = new Option<bool>("--include-other-models", "Include other embedding models");

        var ask = new Command("ask", "Semantic question over indexed code")
        {
            questionArg, repoOpt, branchOpt, recordTypeOpt, featureFlagOpt, topKOpt, minScoreOpt, formatOpt,
            includeStaleOpt, includeOtherModelsOpt, configOption,
        };
        ask.SetHandler(async ctx =>
        {
            var recordTypeStrings = ctx.ParseResult.GetValueForOption(recordTypeOpt) ?? Array.Empty<string>();
            RecordType[] recordTypes;
            try
            {
                recordTypes = recordTypeStrings.Select(RecordTypes.FromWire).ToArray();
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Invalid --record-type: {ex.Message}");
                ctx.ExitCode = ExitUserError;
                return;
            }
            var req = new SearchRequest
            {
                Question = ctx.ParseResult.GetValueForArgument(questionArg),
                Repositories = ctx.ParseResult.GetValueForOption(repoOpt) ?? Array.Empty<string>(),
                Branch = ctx.ParseResult.GetValueForOption(branchOpt),
                RecordTypes = recordTypes,
                FeatureFlag = ctx.ParseResult.GetValueForOption(featureFlagOpt),
                TopK = ctx.ParseResult.GetValueForOption(topKOpt),
                MinScore = ctx.ParseResult.GetValueForOption(minScoreOpt),
                Format = ctx.ParseResult.GetValueForOption(formatOpt) ?? "text",
                IncludeStale = ctx.ParseResult.GetValueForOption(includeStaleOpt),
                IncludeOtherModels = ctx.ParseResult.GetValueForOption(includeOtherModelsOpt),
            };
            var configPath = ctx.ParseResult.GetValueForOption(configOption);
            ctx.ExitCode = await AskCommand.RunAsync(req, configPath, ctx.GetCancellationToken());
        });
        return ask;
    }
}
