using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class RepositoryLoaderTests : IDisposable
{
    private readonly string _temp;

    public RepositoryLoaderTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "codekb-loader-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose() { try { Directory.Delete(_temp, true); } catch { } }

    [Fact]
    public async Task LoadFromPath_ReturnsExpectedRepo()
    {
        var loader = new RepositoryLoader();
        var src = new RepoSource(null, _temp, "develop");
        var r = await loader.LoadAsync(src, CancellationToken.None);
        r.LocalRoot.Should().Be(_temp);
        r.Branch.Should().Be("develop");
        r.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoadFromPath_NonexistentPath_Throws()
    {
        var loader = new RepositoryLoader();
        var bogus = Path.Combine(Path.GetTempPath(), "definitely-not-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            loader.LoadAsync(new RepoSource(null, bogus, null), CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_NoUrlNoPath_Throws()
    {
        var loader = new RepositoryLoader();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            loader.LoadAsync(new RepoSource(null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task LoadFromUrl_UsesCloneStrategyAndReadsRepo()
    {
        // Set up a real local git repo as our fake "remote"
        var fakeRemote = Path.Combine(_temp, "remote");
        Directory.CreateDirectory(fakeRemote);
        LibGit2Sharp.Repository.Init(fakeRemote);
        var sig = new LibGit2Sharp.Signature("test", "test@test", DateTimeOffset.Now);
        File.WriteAllText(Path.Combine(fakeRemote, "x.cs"), "class X {}");
        using (var repo = new LibGit2Sharp.Repository(fakeRemote))
        {
            LibGit2Sharp.Commands.Stage(repo, "x.cs");
            repo.Commit("initial", sig, sig, new LibGit2Sharp.CommitOptions());
        }

        var dest = Path.Combine(_temp, "clone");
        var loader = new RepositoryLoader((url, target, src) =>
        {
            // Clone the local repo to target.
            LibGit2Sharp.Repository.Clone(url, target);
            return src.Branch ?? "main";
        });

        var loaded = await loader.LoadAsync(new RepoSource(fakeRemote, null, null), CancellationToken.None);
        loaded.OriginalUrl.Should().Be(fakeRemote);
        loaded.CommitSha.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("https://github.com/org/repo", "repo")]
    [InlineData("https://github.com/org/repo.git", "repo")]
    [InlineData("git@github.com:org/svc-a.git", "svc-a")]
    [InlineData("", "repository")]
    public void DeriveRepoName(string url, string expected)
        => RepositoryLoader.DeriveRepoName(url).Should().Be(expected);
}
