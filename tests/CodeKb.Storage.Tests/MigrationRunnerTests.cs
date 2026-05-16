using CodeKb.Storage.Postgres.Migrations;
using FluentAssertions;
using Xunit;

namespace CodeKb.Storage.Tests;

public class MigrationRunnerTests
{
    [Fact]
    public void LoadEmbedded_LoadsBothMigrationsInOrder()
    {
        var migrations = MigrationRunner.LoadEmbedded(dimension: 1536);
        migrations.Should().NotBeEmpty();
        migrations.Select(m => m.Name).Should().Contain("001_init");
        migrations.Select(m => m.Name).Should().Contain("002_indexes");
        migrations[0].Name.Should().StartWith("001");
    }

    [Fact]
    public void LoadEmbedded_SubstitutesDimensionToken()
    {
        var migrations = MigrationRunner.LoadEmbedded(dimension: 2048);
        var init = migrations.Single(m => m.Name == "001_init");
        init.Sql.Should().Contain("vector(2048)");
        init.Sql.Should().NotContain("{{dimension}}");
    }

    [Fact]
    public void LoadEmbedded_ContainsCoreTables()
    {
        var migrations = MigrationRunner.LoadEmbedded(1536);
        var init = migrations.Single(m => m.Name == "001_init");
        init.Sql.Should().Contain("repository");
        init.Sql.Should().Contain("scan_job");
        init.Sql.Should().Contain("code_record");
        init.Sql.Should().Contain("code_embedding");
    }

    [Fact]
    public void LoadEmbedded_IndexesCreated()
    {
        var migrations = MigrationRunner.LoadEmbedded(1536);
        var idx = migrations.Single(m => m.Name == "002_indexes");
        idx.Sql.Should().Contain("code_record_filter_idx");
        idx.Sql.Should().Contain("code_record_unique_idx");
        idx.Sql.Should().Contain("hnsw");
    }

    [Fact]
    public void NullDatabaseInitializer_IsNoOp()
    {
        var init = new NullDatabaseInitializer();
        var task = init.InitializeAsync(CancellationToken.None);
        task.IsCompletedSuccessfully.Should().BeTrue();
    }
}
