using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn.Detection;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class MoreFeatureFlagDetectorTests
{
    private readonly FeatureFlagDetector _d = new();
    private readonly ScanOptions _opts = new();

    [Fact]
    public void Constant_NonStringLiteral_Ignored()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { public const string X = SomeMethod(); }");
        _d.Detect(tree, null, _opts).Should().BeEmpty();
    }

    [Fact]
    public void Constant_NonPublicNonConst_Ignored()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { public static string X = \"v\"; }");
        _d.Detect(tree, null, _opts).Should().BeEmpty();
    }

    [Fact]
    public void Constant_EmptyValue_Ignored()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { public const string X = \"\"; }");
        _d.Detect(tree, null, _opts).Should().BeEmpty();
    }

    [Fact]
    public void Invocation_DefaultValueExtracted()
    {
        var tree = CSharpSyntaxTree.ParseText(@"
class C { ILaunchDarklyClient _launchDarklyClient; void M() {
    _launchDarklyClient.BoolVariation(""Flag"", null, false);
}}");
        var hits = _d.Detect(tree, null, _opts);
        hits.Should().NotBeEmpty();
        hits[0].DefaultValue.Should().BeOneOf("false", "False");
    }

    [Fact]
    public void Detect_IdentifierExpression_NoReceiver_Skipped()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { IsEnabled(\"X\"); } }");
        _d.Detect(tree, null, _opts).Should().BeEmpty();
    }
}
