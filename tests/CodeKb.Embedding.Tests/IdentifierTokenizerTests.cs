using CodeKb.Embedding;
using FluentAssertions;
using Xunit;

namespace CodeKb.Embedding.Tests;

public class IdentifierTokenizerTests
{
    [Fact]
    public void Split_PascalCase_BreaksOnUppercaseBoundary()
    {
        var tokens = IdentifierTokenizer.Split("PrePaidAccount");
        tokens.Should().Contain(new[] { "PrePaidAccount", "Pre", "Paid", "Account" });
    }

    [Fact]
    public void Split_camelCase_BreaksOnUppercaseBoundary()
    {
        var tokens = IdentifierTokenizer.Split("processPaymentAsync");
        tokens.Should().Contain(new[] { "processPaymentAsync", "process", "Payment", "Async" });
    }

    [Fact]
    public void Split_Acronym_FollowedByWord()
    {
        var tokens = IdentifierTokenizer.Split("XMLParser");
        tokens.Should().Contain(new[] { "XMLParser", "XML", "Parser" });
    }

    [Fact]
    public void Split_KebabCase()
    {
        var tokens = IdentifierTokenizer.Split("process-payment");
        tokens.Should().Contain(new[] { "process-payment", "process", "payment" });
    }

    [Fact]
    public void Split_SnakeCase()
    {
        var tokens = IdentifierTokenizer.Split("is_enabled");
        tokens.Should().Contain(new[] { "is_enabled", "is", "enabled" });
    }

    [Fact]
    public void Split_DottedName_BreaksOnDots()
    {
        var tokens = IdentifierTokenizer.Split("Acme.Workflow.WorkflowService");
        tokens.Should().Contain(new[] { "Acme", "Workflow", "WorkflowService", "Service" });
    }

    [Fact]
    public void Split_PathLike_BreaksOnSlashes()
    {
        var tokens = IdentifierTokenizer.Split("src/Workflow/Service.cs");
        tokens.Should().Contain(new[] { "src", "Workflow", "Service", "cs" });
    }

    [Fact]
    public void Split_EmptyOrNull_ReturnsEmpty()
    {
        IdentifierTokenizer.Split(null).Should().BeEmpty();
        IdentifierTokenizer.Split("").Should().BeEmpty();
        IdentifierTokenizer.Split("   ").Should().BeEmpty();
    }

    [Fact]
    public void Split_PlainWord_ReturnsSelf()
    {
        var tokens = IdentifierTokenizer.Split("Plain");
        tokens.Should().BeEquivalentTo(new[] { "Plain" });
    }

    [Fact]
    public void Split_DigitBoundary_Splits()
    {
        var tokens = IdentifierTokenizer.Split("HttpV2Client");
        tokens.Should().Contain(new[] { "Http", "V", "2", "Client" });
    }

    [Fact]
    public void Split_AllUppercaseAcronym_Preserved()
    {
        var tokens = IdentifierTokenizer.Split("ABC");
        tokens.Should().Contain("ABC");
    }

    [Fact]
    public void Split_DeduplicatesTokens()
    {
        var tokens = IdentifierTokenizer.Split("AccountAccount");
        tokens.Count(t => t == "Account").Should().Be(1);
    }
}
