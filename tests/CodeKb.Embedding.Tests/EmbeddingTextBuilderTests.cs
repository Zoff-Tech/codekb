using CodeKb.Contracts;
using CodeKb.Embedding;
using FluentAssertions;
using Xunit;

namespace CodeKb.Embedding.Tests;

public class EmbeddingTextBuilderTests
{
    private static CodeRecord MakeRecord(Action<Dictionary<string, object?>>? modify = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["RepositoryName"] = "platform-service",
            ["Branch"] = "main",
            ["CommitSha"] = "abc1234",
            ["FilePath"] = "src/Workflow.cs",
            ["LineStart"] = 42,
            ["LineEnd"] = 78,
            ["RecordType"] = RecordType.FeatureFlagUsage,
            ["Namespace"] = "Acme.Workflow",
            ["ClassName"] = "WorkflowService",
            ["MethodName"] = "ProcessAsync",
            ["SymbolName"] = "Acme.Workflow.WorkflowService.ProcessAsync",
            ["FeatureFlagName"] = "EnableNewWorkflow",
            ["UsageType"] = FeatureFlagUsageType.RuntimeBranch,
            ["Summary"] = "Routes funding through the new workflow.",
            ["CodeSnippet"] = "if (_featureFlags.IsEnabled(\"EnableNewWorkflow\")) { Process(); }",
        };
        modify?.Invoke(fields);
        return new CodeRecord
        {
            RepositoryName = (string)fields["RepositoryName"]!,
            Branch = (string)fields["Branch"]!,
            CommitSha = (string)fields["CommitSha"]!,
            FilePath = (string)fields["FilePath"]!,
            LineStart = (int)fields["LineStart"]!,
            LineEnd = (int)fields["LineEnd"]!,
            RecordType = (RecordType)fields["RecordType"]!,
            Namespace = (string?)fields["Namespace"],
            ClassName = (string?)fields["ClassName"],
            MethodName = (string?)fields["MethodName"],
            SymbolName = (string?)fields["SymbolName"],
            FeatureFlagName = (string?)fields["FeatureFlagName"],
            UsageType = (FeatureFlagUsageType?)fields["UsageType"],
            Summary = (string)fields["Summary"]!,
            CodeSnippet = (string)fields["CodeSnippet"]!,
        };
    }

    [Fact]
    public void Build_RendersExpectedTemplate()
    {
        var rec = MakeRecord();
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().Contain("Repository: platform-service");
        text.Should().Contain("File: src/Workflow.cs:42-78");
        text.Should().Contain("Language: csharp");
        text.Should().Contain("Record Type: feature_flag_usage");
        text.Should().Contain("Feature Flag: EnableNewWorkflow");
        text.Should().Contain("Usage Type: runtime_branch");
        text.Should().Contain("Summary: Routes");
        text.Should().Contain("Code Snippet:");
    }

    [Fact]
    public void Build_OmitsEmptyFields()
    {
        var rec = MakeRecord(d =>
        {
            d["Namespace"] = null;
            d["MethodName"] = null;
            d["FeatureFlagName"] = null;
            d["UsageType"] = null;
        });
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().NotContain("Namespace:");
        text.Should().NotContain("Method:");
        text.Should().NotContain("Feature Flag:");
        text.Should().NotContain("Usage Type:");
    }

    [Fact]
    public void Build_EmptyCodeSnippet_StillReturnsHeader()
    {
        var rec = MakeRecord(d => d["CodeSnippet"] = "");
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().NotContain("Code Snippet:");
    }

    [Fact]
    public void Build_IncludesTokensLine_WithSplitIdentifiers()
    {
        var rec = MakeRecord(d =>
        {
            d["ClassName"] = "PrePaidAccount";
            d["MethodName"] = "ProcessPaymentAsync";
            d["FeatureFlagName"] = "enable-new-workflow";
        });
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().Contain("Tokens:");
        text.Should().Contain("Pre");
        text.Should().Contain("Paid");
        text.Should().Contain("Account");
        text.Should().Contain("Process");
        text.Should().Contain("Payment");
        text.Should().Contain("Async");
        text.Should().Contain("enable");
        text.Should().Contain("workflow");
    }

    [Fact]
    public void Build_TokensLine_OmittedWhenNoIdentifierFields()
    {
        var rec = MakeRecord(d =>
        {
            d["Namespace"] = null;
            d["ClassName"] = null;
            d["MethodName"] = null;
            d["SymbolName"] = null;
            d["FeatureFlagName"] = null;
            d["FilePath"] = "";
        });
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().NotContain("Tokens:");
    }
}
