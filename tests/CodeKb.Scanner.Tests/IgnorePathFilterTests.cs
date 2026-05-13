using CodeKb.Scanner.Roslyn;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class IgnorePathFilterTests
{
    private readonly IgnorePathFilter _filter = new(new[] { "bin", "obj", ".git", "node_modules", "packages" });

    [Theory]
    [InlineData("bin/Debug/foo.dll")]
    [InlineData("obj/temp.cs")]
    [InlineData(".git/HEAD")]
    [InlineData("src/bin/x.cs")]
    [InlineData("project/obj/y.cs")]
    [InlineData("src\\obj\\thing.cs")]
    public void Ignored(string path) => _filter.IsIgnored(path).Should().BeTrue();

    [Theory]
    [InlineData("src/Service.cs")]
    [InlineData("README.md")]
    [InlineData("binx/file.cs")]   // not exact segment match
    public void NotIgnored(string path) => _filter.IsIgnored(path).Should().BeFalse();

    [Fact]
    public void EmptySegments_Filtered()
    {
        var f = new IgnorePathFilter(new[] { "", "  ", "bin" });
        f.IsIgnored("bin/x").Should().BeTrue();
        f.IsIgnored("src/x").Should().BeFalse();
    }

    [Fact]
    public void ExactRootSegment_Ignored()
    {
        new IgnorePathFilter(new[] { "bin" }).IsIgnored("bin").Should().BeTrue();
    }
}
