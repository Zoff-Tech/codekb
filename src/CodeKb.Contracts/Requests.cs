namespace CodeKb.Contracts;

public sealed record ScanRequest
{
    public string? RepoUrl { get; init; }
    public string? Path { get; init; }
    public string? Branch { get; init; }
    public IReadOnlyList<string> SearchTerms { get; init; } = Array.Empty<string>();
    public bool Force { get; init; }

    public void Validate()
    {
        if (RepoUrl is null && Path is null)
            throw new ArgumentException("Either --repo or --path must be supplied.");
        if (RepoUrl is not null && Path is not null)
            throw new ArgumentException("--repo and --path are mutually exclusive.");
    }
}

public sealed record SearchRequest
{
    public required string Question { get; init; }
    public IReadOnlyList<string> Repositories { get; init; } = Array.Empty<string>();
    public string? Branch { get; init; }
    public IReadOnlyList<RecordType> RecordTypes { get; init; } = Array.Empty<RecordType>();
    public string? FeatureFlag { get; init; }
    public int TopK { get; init; } = 10;
    public double? MinScore { get; init; }
    public string Format { get; init; } = "text";
    public bool IncludeStale { get; init; }
    public bool IncludeOtherModels { get; init; }
}

public sealed record RepositoryRow(Guid Id, string Name, string? Url, string? LocalPath, string Branch, string CommitSha);

public sealed record ScanJobOutcome(
    ScanStatus Status,
    int FilesScanned,
    int RecordsCreated,
    int RecordsFailed,
    int RecordsRedactionFailed,
    int EmbeddingsCreated,
    int FeatureFlagMatches,
    string? ErrorMessage);

public sealed record ScanResult
{
    public required Guid ScanJobId { get; init; }
    public required string RepositoryName { get; init; }
    public required string Branch { get; init; }
    public required string CommitSha { get; init; }
    public required ScanJobOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public bool AlreadyIndexed { get; init; }
}

public sealed record SearchHit(
    string Repository,
    string Branch,
    string CommitSha,
    string FilePath,
    int LineStart,
    int LineEnd,
    string? SymbolName,
    RecordType RecordType,
    string Summary,
    string CodeSnippet,
    double Score);
