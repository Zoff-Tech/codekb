using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn.Detection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class FeatureFlagDetectorTests
{
    private readonly FeatureFlagDetector _d = new();
    private readonly ScanOptions _opts = new();

    [Fact]
    public void ConstantString_DetectedAsConstantDefinition()
    {
        var code = @"
public class Flags {
    public const string EnableNewWorkflow = ""EnableNewWorkflow"";
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var hits = _d.Detect(tree, semanticModel: null, _opts);
        hits.Should().ContainSingle(h => h.UsageType == FeatureFlagUsageType.ConstantDefinition
            && h.FlagName == "EnableNewWorkflow");
    }

    [Fact]
    public void Invocation_WithMatchingMethodAndReceiverName_Detected()
    {
        var code = @"
class C {
    private IFeatureManager _featureManager;
    public void M() { _featureManager.IsEnabled(""EnableNewWorkflow""); }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var hits = _d.Detect(tree, semanticModel: null, _opts);
        hits.Should().ContainSingle(h => h.UsageType == FeatureFlagUsageType.RuntimeBranch
            && h.FlagName == "EnableNewWorkflow"
            && h.MethodName == "IsEnabled");
    }

    [Fact]
    public void Invocation_WithUnknownReceiver_NotDetected_InSyntaxOnly()
    {
        var code = @"class C { public void M(object thing) { thing.IsEnabled(""X""); } }";
        var tree = CSharpSyntaxTree.ParseText(code);
        var hits = _d.Detect(tree, semanticModel: null, _opts);
        hits.Should().BeEmpty();
    }

    [Fact]
    public void Invocation_WithMatchingMethodAndSemanticReceiverType_Detected()
    {
        var src = @"
public interface IFeatureManager { bool IsEnabled(string n); }
public class M {
    private readonly IFeatureManager _ff;
    public M(IFeatureManager ff) { _ff = ff; }
    public void Do() { _ff.IsEnabled(""FF""); }
}";
        var tree = CSharpSyntaxTree.ParseText(src);
        var compilation = CSharpCompilation.Create("t",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var sm = compilation.GetSemanticModel(tree);
        var hits = _d.Detect(tree, sm, _opts);
        hits.Should().ContainSingle(h => h.FlagName == "FF");
    }

    [Fact]
    public void Invocation_FirstArgNotString_NotDetected()
    {
        var code = @"class C { IFeatureManager _featureFlags; void M(int i) { _featureFlags.IsEnabled(i); } }";
        var tree = CSharpSyntaxTree.ParseText(code);
        _d.Detect(tree, null, _opts).Should().BeEmpty();
    }

    [Fact]
    public void Invocation_NoArgs_NotDetected()
    {
        var code = @"class C { IFeatureManager _featureFlags; void M() { _featureFlags.IsEnabled(); } }";
        var tree = CSharpSyntaxTree.ParseText(code);
        _d.Detect(tree, null, _opts).Should().BeEmpty();
    }

    [Fact]
    public void ExtractMethodName_HandlesMemberAccessAndIdentifier()
    {
        var t1 = CSharpSyntaxTree.ParseText("class C { void M() { obj.Foo(); } }");
        var inv1 = t1.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        FeatureFlagDetector.ExtractMethodName(inv1).Should().Be("Foo");

        var t2 = CSharpSyntaxTree.ParseText("class C { void M() { Foo(); } }");
        var inv2 = t2.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        FeatureFlagDetector.ExtractMethodName(inv2).Should().Be("Foo");
    }

    [Fact]
    public void HeuristicReceiverMatch_MatchesPrefixedI()
    {
        var set = new HashSet<string> { "IFeatureManager" };
        FeatureFlagDetector.HeuristicReceiverMatch("_featureManager", set).Should().BeTrue();
        FeatureFlagDetector.HeuristicReceiverMatch("featureManager", set).Should().BeTrue();
        FeatureFlagDetector.HeuristicReceiverMatch("_other", set).Should().BeFalse();
    }

    [Fact]
    public void HeuristicReceiverMatch_EmptyReceiver_False()
    {
        FeatureFlagDetector.HeuristicReceiverMatch("", new HashSet<string>()).Should().BeFalse();
    }

    [Fact]
    public void ConstantNotStringType_Ignored()
    {
        var code = "class C { public const int X = 42; }";
        var tree = CSharpSyntaxTree.ParseText(code);
        _d.Detect(tree, null, _opts).Should().BeEmpty();
    }
}
