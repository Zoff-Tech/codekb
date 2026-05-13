namespace CodeKb.Contracts;

public sealed record ScanOptions
{
    public IReadOnlyList<string> IgnorePaths { get; init; } = new[] { "bin", "obj", ".git", "node_modules", "packages" };
    public IReadOnlyList<string> FeatureFlagMethodNames { get; init; } = new[] { "IsEnabled", "IsEnabledAsync", "IsFeatureEnabled", "BoolVariation" };
    public IReadOnlyList<string> FeatureFlagClientNames { get; init; } = new[] { "IFeatureFlagService", "IFeatureManager", "ILaunchDarklyClient" };
    public int Parallelism { get; init; } = 4;
    public int MaxFileSizeKb { get; init; } = 512;
    public IReadOnlyList<string> SearchTerms { get; init; } = Array.Empty<string>();
}

public sealed record LoadedRepository(string LocalRoot, string Name, string Branch, string CommitSha, string? OriginalUrl);

public sealed record RepoSource(string? Url, string? Path, string? Branch);
