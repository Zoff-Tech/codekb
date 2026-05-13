using CodeKb.Contracts;
using FluentAssertions;
using Xunit;

namespace CodeKb.Contracts.Tests;

public class RecordTypesTests
{
    [Theory]
    [InlineData(RecordType.FileSummary, "file_summary")]
    [InlineData(RecordType.ClassSummary, "class_summary")]
    [InlineData(RecordType.MethodSummary, "method_summary")]
    [InlineData(RecordType.FeatureFlagUsage, "feature_flag_usage")]
    [InlineData(RecordType.SearchTermMatch, "search_term_match")]
    [InlineData(RecordType.TestReference, "test_reference")]
    [InlineData(RecordType.ConfigurationReference, "configuration_reference")]
    public void ToWire_Roundtrips(RecordType type, string wire)
    {
        type.ToWire().Should().Be(wire);
        RecordTypes.FromWire(wire).Should().Be(type);
    }

    [Fact]
    public void FromWire_Unknown_Throws()
    {
        Assert.Throws<ArgumentException>(() => RecordTypes.FromWire("nope"));
    }

    [Fact]
    public void ToWire_InvalidEnumValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((RecordType)999).ToWire());
    }

    [Theory]
    [InlineData(FeatureFlagUsageType.RuntimeBranch, "runtime_branch")]
    [InlineData(FeatureFlagUsageType.ConstantDefinition, "constant_definition")]
    [InlineData(FeatureFlagUsageType.Injection, "injection")]
    [InlineData(FeatureFlagUsageType.Config, "config")]
    public void UsageType_ToWire(FeatureFlagUsageType t, string wire) => t.ToWire().Should().Be(wire);

    [Fact]
    public void UsageType_InvalidEnumValue_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ((FeatureFlagUsageType)999).ToWire());

    [Theory]
    [InlineData(SearchMatchKind.Identifier, "identifier")]
    [InlineData(SearchMatchKind.StringLiteral, "string_literal")]
    [InlineData(SearchMatchKind.Comment, "comment")]
    [InlineData(SearchMatchKind.XmlDoc, "xml_doc")]
    public void MatchKind_ToWire(SearchMatchKind k, string wire) => k.ToWire().Should().Be(wire);

    [Fact]
    public void MatchKind_InvalidEnumValue_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ((SearchMatchKind)999).ToWire());
}
