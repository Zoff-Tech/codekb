using CodeKb.Contracts;
using FluentAssertions;
using Xunit;

namespace CodeKb.Contracts.Tests;

public class EnumCoverageTests
{
    [Fact]
    public void AllSymbolKinds_AreEnumerable()
    {
        var kinds = Enum.GetValues<SymbolKind>();
        kinds.Should().Contain(SymbolKind.Class);
        kinds.Should().Contain(SymbolKind.Interface);
        kinds.Should().Contain(SymbolKind.Record);
        kinds.Should().Contain(SymbolKind.Enum);
        kinds.Should().Contain(SymbolKind.Struct);
        kinds.Should().Contain(SymbolKind.Method);
        kinds.Should().Contain(SymbolKind.Property);
        kinds.Should().Contain(SymbolKind.Field);
        kinds.Should().Contain(SymbolKind.File);
        kinds.Should().Contain(SymbolKind.None);
    }

    [Fact]
    public void AllEmbeddingStatuses_AreEnumerable()
    {
        var s = Enum.GetValues<EmbeddingStatus>();
        s.Should().Contain(EmbeddingStatus.Pending);
        s.Should().Contain(EmbeddingStatus.Embedded);
        s.Should().Contain(EmbeddingStatus.Failed);
    }

    [Fact]
    public void AllScanStatuses_AreEnumerable()
    {
        var s = Enum.GetValues<ScanStatus>();
        s.Should().Contain(ScanStatus.Pending);
        s.Should().Contain(ScanStatus.Running);
        s.Should().Contain(ScanStatus.Completed);
        s.Should().Contain(ScanStatus.Failed);
    }

    [Fact]
    public void AllFileKinds_AreEnumerable()
    {
        var k = Enum.GetValues<FileKind>();
        k.Should().Contain(FileKind.Production);
        k.Should().Contain(FileKind.Test);
        k.Should().Contain(FileKind.Generated);
        k.Should().Contain(FileKind.Configuration);
    }
}
