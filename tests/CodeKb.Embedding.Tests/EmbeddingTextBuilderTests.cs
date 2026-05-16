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
            ["RecordType"] = RecordType.MethodSummary,
            ["Namespace"] = "Acme.Workflow",
            ["ClassName"] = "WorkflowService",
            ["MethodName"] = "ProcessAsync",
            ["SymbolName"] = "Acme.Workflow.WorkflowService.ProcessAsync",
            ["Summary"] = "Routes funding through the new workflow.",
            ["CodeSnippet"] = "if (_flags.IsEnabled(\"EnableNewWorkflow\")) { Process(); }",
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
        text.Should().Contain("Record Type: method_summary");
        text.Should().Contain("Class: WorkflowService");
        text.Should().Contain("Method: ProcessAsync");
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
        });
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().NotContain("Namespace:");
        text.Should().NotContain("Method:");
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
        });
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().Contain("Tokens:");
        text.Should().Contain("Pre");
        text.Should().Contain("Paid");
        text.Should().Contain("Account");
        text.Should().Contain("Process");
        text.Should().Contain("Payment");
        text.Should().Contain("Async");
    }

    [Fact]
    public void Build_FeatureFlagName_StillRetrievableThroughSnippetAndSymbol()
    {
        // After the feature-flag feature was removed, flag names are no longer first-class fields.
        // They flow into embeddings via the source snippet (where they appear as string literals or
        // identifiers) and via the symbol name (e.g. EnableNewWorkflow when used as a search term).
        var rec = MakeRecord(d =>
        {
            d["SymbolName"] = "EnableNewWorkflow";
            d["CodeSnippet"] = "if (_flags.IsEnabled(\"EnableNewWorkflow\")) { Process(); }";
        });
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().Contain("EnableNewWorkflow");
        text.Should().Contain("Tokens:");
        text.Should().Contain("Enable");
        text.Should().Contain("Workflow");
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
            d["FilePath"] = "";
        });
        var text = EmbeddingTextBuilder.Build(rec);
        text.Should().NotContain("Tokens:");
    }
}
