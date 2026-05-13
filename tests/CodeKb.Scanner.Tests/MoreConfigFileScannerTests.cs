using CodeKb.Scanner.Roslyn.Detection;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class MoreConfigFileScannerTests
{
    private readonly ConfigFileScanner _s = new();

    [Fact]
    public void Json_ArraysWalked()
    {
        var content = @"{ ""FeatureFlags"": [ { ""X"": true } ] }";
        var m = _s.Scan("config.json", content, Array.Empty<string>());
        m.Should().Contain(x => x.Key == "X");
    }

    [Fact]
    public void Json_BooleansFalseStringified()
    {
        var content = @"{ ""FeatureFlags"": { ""Off"": false } }";
        var m = _s.Scan("config.json", content, Array.Empty<string>());
        m.Single().Value.Should().Be("false");
    }

    [Fact]
    public void Json_NumberPreservedAsRawText()
    {
        var content = @"{ ""FeatureFlags"": { ""N"": 42 } }";
        var m = _s.Scan("config.json", content, Array.Empty<string>());
        m.Single().Value.Should().Be("42");
    }

    [Fact]
    public void Json_NestedFeatureSection()
    {
        var content = @"{ ""Outer"": { ""Toggles"": { ""Inner"": ""yes"" } } }";
        var m = _s.Scan("config.json", content, Array.Empty<string>());
        m.Should().Contain(x => x.Key == "Inner");
    }

    [Fact]
    public void Env_EmptyLines_Skipped()
    {
        var m = _s.Scan(".env", "\n\n\n", Array.Empty<string>());
        m.Should().BeEmpty();
    }

    [Fact]
    public void Env_LineWithoutEquals_Skipped()
    {
        var m = _s.Scan(".env", "JUSTKEY\nNAME=v", Array.Empty<string>());
        m.Should().HaveCount(1);
    }
}
