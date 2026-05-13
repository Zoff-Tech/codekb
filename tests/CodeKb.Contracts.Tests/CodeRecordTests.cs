using CodeKb.Contracts;
using FluentAssertions;
using Xunit;

namespace CodeKb.Contracts.Tests;

public class CodeRecordTests
{
    [Fact]
    public void CodeRecord_DefaultsAndRequiredFields()
    {
        var rec = new CodeRecord
        {
            RepositoryName = "r",
            Branch = "main",
            CommitSha = "sha",
            FilePath = "f.cs",
            LineStart = 1,
            LineEnd = 10,
            RecordType = RecordType.FileSummary,
            Summary = "s",
            CodeSnippet = "snip",
        };
        rec.Id.Should().NotBe(Guid.Empty);
        rec.EmbeddingStatus.Should().Be(EmbeddingStatus.Pending);
        rec.MetadataJson.Should().Be("{}");
        rec.SymbolKind.Should().Be(SymbolKind.None);
    }

    [Fact]
    public void CodeRecord_WithExpression_Clones()
    {
        var rec = new CodeRecord
        {
            RepositoryName = "r", Branch = "b", CommitSha = "s",
            FilePath = "f", LineStart = 1, LineEnd = 2,
            RecordType = RecordType.FileSummary, Summary = "x", CodeSnippet = "y",
        };
        var clone = rec with { Branch = "main2" };
        clone.Branch.Should().Be("main2");
        clone.RepositoryName.Should().Be("r");
    }

    [Fact]
    public void EmbeddingRow_HoldsAllFields()
    {
        var row = new EmbeddingRow(Guid.NewGuid(), "m", "v", 3, new[] { 1f, 2f, 3f }, "t");
        row.EmbeddingModel.Should().Be("m");
        row.Vector.Should().HaveCount(3);
    }
}
