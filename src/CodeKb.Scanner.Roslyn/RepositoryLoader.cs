using CodeKb.Contracts;
using LibGit2Sharp;

namespace CodeKb.Scanner.Roslyn;

public interface IRepositoryLoader
{
    Task<LoadedRepository> LoadAsync(RepoSource source, CancellationToken ct);
}

public sealed class RepositoryLoader : IRepositoryLoader
{
    private readonly Func<string, string, RepoSource, string> _cloneStrategy;

    public RepositoryLoader() : this(DefaultClone) { }

    internal RepositoryLoader(Func<string, string, RepoSource, string> cloneStrategy)
    {
        _cloneStrategy = cloneStrategy;
    }

    public Task<LoadedRepository> LoadAsync(RepoSource source, CancellationToken ct)
    {
        if (source.Path is not null)
            return Task.FromResult(LoadFromPath(source.Path, source.Branch));
        if (source.Url is not null)
            return Task.FromResult(LoadFromUrl(source.Url, source.Branch));
        throw new ArgumentException("RepoSource must have either Url or Path");
    }

    internal static LoadedRepository LoadFromPath(string path, string? branchHint)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Repository path does not exist: {path}");

        var name = new DirectoryInfo(path).Name;
        var branch = branchHint ?? string.Empty;
        var commitSha = string.Empty;

        var gitDir = Path.Combine(path, ".git");
        if (Directory.Exists(gitDir) || File.Exists(gitDir))
        {
            try
            {
                using var repo = new Repository(path);
                branch = branchHint ?? repo.Head.FriendlyName;
                commitSha = repo.Head.Tip?.Sha ?? string.Empty;
            }
            catch (RepositoryNotFoundException)
            {
                // not a real repo: ignore
            }
        }

        return new LoadedRepository(
            LocalRoot: path,
            Name: name,
            Branch: string.IsNullOrEmpty(branch) ? "main" : branch,
            CommitSha: string.IsNullOrEmpty(commitSha) ? "0000000" : commitSha,
            OriginalUrl: null);
    }

    private LoadedRepository LoadFromUrl(string url, string? branch)
    {
        var dest = Path.Combine(Path.GetTempPath(), "codekb-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        var actualBranch = _cloneStrategy(url, dest, new RepoSource(url, null, branch));
        using var repo = new Repository(dest);
        return new LoadedRepository(
            LocalRoot: dest,
            Name: DeriveRepoName(url),
            Branch: actualBranch,
            CommitSha: repo.Head.Tip?.Sha ?? string.Empty,
            OriginalUrl: url);
    }

    internal static string DefaultClone(string url, string dest, RepoSource src)
    {
        var co = new CloneOptions { IsBare = false };
        if (src.Branch is not null)
        {
            co.BranchName = src.Branch;
        }
        // Depth is exposed via internal options on some LibGit2Sharp versions; shallow clone is best-effort.

        var token = Environment.GetEnvironmentVariable("GIT_TOKEN");
        var user = Environment.GetEnvironmentVariable("GIT_USERNAME") ?? "x-access-token";
        if (!string.IsNullOrEmpty(token) && !url.StartsWith("git@"))
        {
            co.FetchOptions.CredentialsProvider = (_, _, _) =>
                new UsernamePasswordCredentials { Username = user, Password = token };
        }

        Repository.Clone(url, dest, co);
        return src.Branch ?? "main";
    }

    internal static string DeriveRepoName(string url)
    {
        var trimmed = url.TrimEnd('/');
        var idx = trimmed.LastIndexOfAny(new[] { '/', ':' });
        var name = idx >= 0 ? trimmed.Substring(idx + 1) : trimmed;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        return name.Length == 0 ? "repository" : name;
    }
}
