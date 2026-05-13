namespace CodeKb.Core.Configuration;

public sealed class CodeKbOptions
{
    public StorageOptions Storage { get; set; } = new();
    public EmbeddingOptions Embedding { get; set; } = new();
    public ScannerOptions Scanner { get; set; } = new();
}

public sealed class StorageOptions
{
    public string PostgresConnectionString { get; set; } = string.Empty;
}

public sealed class EmbeddingOptions
{
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "text-embedding-3-small";
    public string ModelVersion { get; set; } = "1";
    public int Dimension { get; set; } = 1536;
    public int BatchSize { get; set; } = 256;
    public int MaxRetries { get; set; } = 5;
    public double RetryBackoffSeconds { get; set; } = 2;
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
}

public sealed class ScannerOptions
{
    public List<string> IgnorePaths { get; set; } = new() { "bin", "obj", ".git", "node_modules", "packages" };
    public List<string> FeatureFlagMethodNames { get; set; } = new() { "IsEnabled", "IsEnabledAsync", "IsFeatureEnabled", "BoolVariation" };
    public List<string> FeatureFlagClientNames { get; set; } = new() { "IFeatureFlagService", "IFeatureManager", "ILaunchDarklyClient" };
    public int Parallelism { get; set; } = 4;
    public int MaxFileSizeKb { get; set; } = 512;
}
