namespace CodeKb.Contracts;

public sealed record CodeRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RepositoryId { get; init; }
    public Guid ScanJobId { get; init; }
    public required string RepositoryName { get; init; }
    public required string Branch { get; init; }
    public required string CommitSha { get; init; }
    public required string FilePath { get; init; }
    public required int LineStart { get; init; }
    public required int LineEnd { get; init; }
    public required RecordType RecordType { get; init; }
    public string? SymbolName { get; init; }
    public SymbolKind SymbolKind { get; init; } = SymbolKind.None;
    public string? Namespace { get; init; }
    public string? ClassName { get; init; }
    public string? MethodName { get; init; }
    public required string Summary { get; init; }
    public required string CodeSnippet { get; init; }
    public string MetadataJson { get; init; } = "{}";
    public bool IsTestCode { get; init; }
    public bool IsGeneratedCode { get; init; }
    public bool IsStale { get; init; }
    public EmbeddingStatus EmbeddingStatus { get; init; } = EmbeddingStatus.Pending;
}

public sealed record EmbeddingRow(
    Guid CodeRecordId,
    string EmbeddingModel,
    string EmbeddingModelVersion,
    int EmbeddingDimension,
    float[] Vector,
    string EmbeddingText);
