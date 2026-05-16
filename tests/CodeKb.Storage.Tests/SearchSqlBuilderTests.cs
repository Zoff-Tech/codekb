using CodeKb.Contracts;
using CodeKb.Storage.Postgres;
using FluentAssertions;
using Xunit;

namespace CodeKb.Storage.Tests;

public class SearchSqlBuilderTests
{
    private static SearchQuery Base(params Action<SearchQueryBuilder>[] modifiers)
    {
        var b = new SearchQueryBuilder();
        foreach (var m in modifiers) m(b);
        return b.Build();
    }

    private sealed class SearchQueryBuilder
    {
        public float[] Vec = new[] { 1f, 2f, 3f };
        public string Model = "fake-model";
        public int TopK = 10;
        public List<string> Repos = new();
        public string? Branch;
        public List<RecordType> Types = new();
        public bool Stale;
        public bool Other;
        public SearchQuery Build() => new(Vec, Model, TopK, Repos, Branch, Types, Stale, Other);
    }

    [Fact]
    public void Build_DefaultQuery_ContainsCoreSelectAndOrder()
    {
        var built = SearchSqlBuilder.Build(Base());
        built.Sql.Should().Contain("SELECT");
        built.Sql.Should().Contain("FROM code_record cr");
        built.Sql.Should().Contain("ORDER BY ce.embedding_vector <=> @query_vec");
        built.Sql.Should().Contain("LIMIT @top_k");
        built.Sql.Should().Contain("is_stale = FALSE");
        built.Sql.Should().Contain("embedding_model = @embedding_model");
    }

    [Fact]
    public void Build_IncludeStale_OmitsStaleFilter()
    {
        var built = SearchSqlBuilder.Build(Base(b => b.Stale = true));
        built.Sql.Should().NotContain("is_stale = FALSE");
    }

    [Fact]
    public void Build_IncludeOtherModels_OmitsModelFilter()
    {
        var built = SearchSqlBuilder.Build(Base(b => b.Other = true));
        built.Sql.Should().NotContain("embedding_model = @embedding_model");
    }

    [Fact]
    public void Build_WithRepoFilter_AddsParam()
    {
        var built = SearchSqlBuilder.Build(Base(b => b.Repos = new() { "platform-service" }));
        built.Sql.Should().Contain("cr.repository_name = ANY(@repos)");
        built.Parameters.Should().Contain(p => p.Name == "repos");
    }

    [Fact]
    public void Build_WithBranch_AddsParam()
    {
        var built = SearchSqlBuilder.Build(Base(b => b.Branch = "main"));
        built.Sql.Should().Contain("cr.branch = @branch");
    }

    [Fact]
    public void Build_WithRecordTypes_AddsParam()
    {
        var built = SearchSqlBuilder.Build(Base(b => b.Types = new() { RecordType.ClassSummary, RecordType.MethodSummary }));
        built.Sql.Should().Contain("cr.record_type = ANY(@record_types)");
        var p = built.Parameters.Single(x => x.Name == "record_types");
        ((string[])p.Value!).Should().Contain("class_summary").And.Contain("method_summary");
    }

    [Fact]
    public void Build_AlwaysContainsQueryVecAndTopK()
    {
        var built = SearchSqlBuilder.Build(Base());
        built.Parameters.Should().Contain(p => p.Name == "query_vec");
        built.Parameters.Should().Contain(p => p.Name == "top_k");
    }
}
