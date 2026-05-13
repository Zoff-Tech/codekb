using CodeKb.Scanner.Roslyn.Detection;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class ConfigFileScannerTests
{
    private readonly ConfigFileScanner _s = new();

    [Fact]
    public void Json_FeatureSection_EmitsRecord()
    {
        var content = @"{ ""FeatureFlags"": { ""EnableNewWorkflow"": true } }";
        var m = _s.Scan("config.json", content, Array.Empty<string>());
        m.Should().Contain(x => x.Key == "EnableNewWorkflow");
    }

    [Fact]
    public void Json_AppSettingsFile_EmitsRecord()
    {
        var content = @"{ ""SomeKey"": ""value"" }";
        var m = _s.Scan("appsettings.json", content, Array.Empty<string>());
        m.Should().Contain(x => x.Key == "SomeKey");
    }

    [Fact]
    public void Json_NonFeatureSection_NoEmit_WithoutSearch()
    {
        var content = @"{ ""Foo"": { ""Bar"": ""baz"" } }";
        var m = _s.Scan("random.json", content, Array.Empty<string>());
        m.Should().BeEmpty();
    }

    [Fact]
    public void Json_SearchTermKey_EmitsEvenInNonFeatureSection()
    {
        var content = @"{ ""Foo"": { ""EnableNewWorkflow"": true } }";
        var m = _s.Scan("random.json", content, new[] { "EnableNewWorkflow" });
        m.Should().Contain(x => x.Key == "EnableNewWorkflow");
    }

    [Fact]
    public void Json_Invalid_ReturnsEmpty()
    {
        var m = _s.Scan("config.json", "{ not valid json", Array.Empty<string>());
        m.Should().BeEmpty();
    }

    [Fact]
    public void Env_Values_AlwaysRedacted()
    {
        var content = "API_KEY=secret123\nOTHER=value";
        var m = _s.Scan(".env", content, Array.Empty<string>());
        m.Should().HaveCount(2);
        m.Should().AllSatisfy(x => x.ValueRedacted.Should().BeTrue());
    }

    [Fact]
    public void Env_SkipsCommentsAndEmpty()
    {
        var content = "# comment\n\nKEY=value";
        var m = _s.Scan(".env", content, Array.Empty<string>());
        m.Should().HaveCount(1);
    }

    [Fact]
    public void Env_WithSearchTerm_OnlyEmitsMatches()
    {
        var content = "FOO=1\nBAR=2";
        var m = _s.Scan(".env", content, new[] { "BAR" });
        m.Should().ContainSingle(x => x.Key == "BAR");
    }

    [Fact]
    public void UnknownExtension_ReturnsEmpty()
    {
        var m = _s.Scan("foo.txt", "anything", Array.Empty<string>());
        m.Should().BeEmpty();
    }

    [Theory]
    [InlineData("appsettings.json", true)]
    [InlineData("appsettings.Production.json", true)]
    [InlineData("config/appsettings.yaml", true)]
    [InlineData("random.json", false)]
    public void IsAppSettingsFile(string path, bool expected)
        => ConfigFileScanner.IsAppSettingsFile(path).Should().Be(expected);

    [Theory]
    [InlineData("Features", true)]
    [InlineData("FeatureFlags", true)]
    [InlineData("Toggles", true)]
    [InlineData("Foo", false)]
    public void IsFeatureSection(string s, bool expected)
        => ConfigFileScanner.IsFeatureSection(s).Should().Be(expected);
}
